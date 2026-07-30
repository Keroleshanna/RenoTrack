using FluentValidation;
using RenoTrack.Application.Angebote.Commands.SubmitAngebotForReview;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Commands.SubmitAngebotForReview;

public class SubmitAngebotForReviewCommandHandlerTests
{
    private const int OwningInspectorId = 5;

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly OwnershipValidator _ownershipValidator = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly FakeEmailSender _emailSender = new();
    private readonly SubmitAngebotForReviewCommandHandler _handler;

    public SubmitAngebotForReviewCommandHandlerTests()
    {
        _handler = new SubmitAngebotForReviewCommandHandler(
            new SubmitAngebotForReviewCommandValidator(),
            _angebotRepository,
            _ownershipValidator,
            _unitOfWork,
            _auditService,
            _emailSender);
    }

    /// <summary>Seeds a Draft Angebot with one section and one item — the minimum required for SubmitForReview to succeed.</summary>
    private Angebot SeedSubmittableAngebot(int createdByInspectorId = OwningInspectorId)
    {
        var angebot = _angebotRepository.Seed(Angebot.Create(leadId: 1, inspectionId: null, "ANG-2026-00001", createdByInspectorId));
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);
        return angebot;
    }

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task HandleAsync_TransitionsAngebotToInReview()
    {
        var angebot = SeedSubmittableAngebot();
        var command = new SubmitAngebotForReviewCommand(angebot.Id, OwningInspectorId);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(AngebotStatus.InReview, result.Status);
        Assert.Equal(AngebotStatus.InReview, angebot.Status);
    }

    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        var angebot = SeedSubmittableAngebot();
        var command = new SubmitAngebotForReviewCommand(angebot.Id, OwningInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_LogsAuditAgainstTheAngebot_NotTheLead()
    {
        var angebot = SeedSubmittableAngebot();
        var command = new SubmitAngebotForReviewCommand(angebot.Id, OwningInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        var call = Assert.Single(_auditService.Calls);
        Assert.Equal("Angebot", call.EntityType);
        Assert.Equal(angebot.Id, call.EntityId);
        Assert.Equal(AuditAction.AngebotSubmittedForReview, call.Action);
        Assert.Equal(OwningInspectorId, call.PerformedByUserId);
    }

    [Fact]
    public async Task HandleAsync_SendsAngebotSubmittedForReviewNotification()
    {
        var angebot = SeedSubmittableAngebot();
        var command = new SubmitAngebotForReviewCommand(angebot.Id, OwningInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        var notification = Assert.Single(_emailSender.AngebotSubmittedForReviewNotifications);
        Assert.Equal(angebot.Id, notification.AngebotId);
        Assert.Equal(angebot.AngebotNumber, notification.AngebotNumber);
        Assert.Equal(angebot.LeadId, notification.LeadId);
    }

    // ---- Not found / ownership / domain guards -----------------------------

    [Fact]
    public async Task HandleAsync_AngebotDoesNotExist_ThrowsNotFoundException()
    {
        var command = new SubmitAngebotForReviewCommand(AngebotId: 999, OwningInspectorId);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_InspectorDoesNotOwnAngebot_ThrowsForbiddenException()
    {
        var angebot = SeedSubmittableAngebot();
        var command = new SubmitAngebotForReviewCommand(angebot.Id, InspectorId: 999);

        await Assert.ThrowsAsync<ForbiddenException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_NoItemsYet_PropagatesDomainGuardFailure_AndSendsNoNotification()
    {
        var angebot = _angebotRepository.Seed(Angebot.Create(1, null, "ANG-2026-00001", OwningInspectorId));
        angebot.AddSection("Pos. 1", 1); // section exists, but has no items
        var command = new SubmitAngebotForReviewCommand(angebot.Id, OwningInspectorId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Empty(_emailSender.AngebotSubmittedForReviewNotifications);
        Assert.Empty(_auditService.Calls);
    }

    [Fact]
    public async Task HandleAsync_AlreadyInReview_PropagatesDomainGuardFailure()
    {
        var angebot = SeedSubmittableAngebot();
        angebot.SubmitForReview();
        var command = new SubmitAngebotForReviewCommand(angebot.Id, OwningInspectorId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    // ---- Validation ----------------------------------------------------

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 0)]
    public async Task HandleAsync_InvalidCommand_ThrowsAndPerformsNoSideEffects(int angebotId, int inspectorId)
    {
        var command = new SubmitAngebotForReviewCommand(angebotId, inspectorId);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
        Assert.Empty(_emailSender.AngebotSubmittedForReviewNotifications);
    }
}
