using FluentValidation;
using RenoTrack.Application.Angebote.Commands.RequestAngebotChanges;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Commands.RequestAngebotChanges;

public class RequestAngebotChangesCommandHandlerTests
{
    private const int OwningInspectorId = 5;
    private const int AdminId = 2;

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly FakeAngebotReviewCommentRepository _reviewCommentRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly FakeEmailSender _emailSender = new();
    private readonly RequestAngebotChangesCommandHandler _handler;

    public RequestAngebotChangesCommandHandlerTests()
    {
        _handler = new RequestAngebotChangesCommandHandler(
            new RequestAngebotChangesCommandValidator(),
            _angebotRepository,
            _reviewCommentRepository,
            _unitOfWork,
            _auditService,
            _emailSender);
    }

    private Angebot SeedAngebotInReview()
    {
        var angebot = _angebotRepository.Seed(Angebot.Create(leadId: 1, inspectionId: null, "ANG-2026-00001", OwningInspectorId));
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);
        angebot.SubmitForReview();
        return angebot;
    }

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task HandleAsync_TransitionsAngebotToChangesRequested()
    {
        var angebot = SeedAngebotInReview();
        var command = new RequestAngebotChangesCommand(angebot.Id, "Please fix the VAT rate.", AdminId);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(AngebotStatus.ChangesRequested, result.Status);
    }

    [Fact]
    public async Task HandleAsync_RecordsReviewedByAdminIdOnAngebot()
    {
        var angebot = SeedAngebotInReview();
        var command = new RequestAngebotChangesCommand(angebot.Id, "Please fix the VAT rate.", AdminId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(AdminId, angebot.ReviewedByAdminId);
    }

    [Fact]
    public async Task HandleAsync_CreatesAnAngebotReviewCommentWithMatchingFields()
    {
        var angebot = SeedAngebotInReview();
        var command = new RequestAngebotChangesCommand(angebot.Id, "Please fix the VAT rate.", AdminId);

        await _handler.HandleAsync(command, CancellationToken.None);

        var comment = Assert.Single(_reviewCommentRepository.AddedComments);
        Assert.Equal(angebot.Id, comment.AngebotId);
        Assert.Equal(AdminId, comment.AdminUserId);
        Assert.Equal("Please fix the VAT rate.", comment.Comment);
    }

    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        var angebot = SeedAngebotInReview();
        var command = new RequestAngebotChangesCommand(angebot.Id, "Please fix the VAT rate.", AdminId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_LogsAuditAgainstTheAngebot()
    {
        var angebot = SeedAngebotInReview();
        var command = new RequestAngebotChangesCommand(angebot.Id, "Please fix the VAT rate.", AdminId);

        await _handler.HandleAsync(command, CancellationToken.None);

        var call = Assert.Single(_auditService.Calls);
        Assert.Equal("Angebot", call.EntityType);
        Assert.Equal(angebot.Id, call.EntityId);
        Assert.Equal(AuditAction.AngebotChangesRequested, call.Action);
        Assert.Equal(AdminId, call.PerformedByUserId);
    }

    [Fact]
    public async Task HandleAsync_SendsAngebotChangesRequestedNotificationToTheOwningInspector()
    {
        var angebot = SeedAngebotInReview();
        var command = new RequestAngebotChangesCommand(angebot.Id, "Please fix the VAT rate.", AdminId);

        await _handler.HandleAsync(command, CancellationToken.None);

        var notification = Assert.Single(_emailSender.AngebotChangesRequestedNotifications);
        Assert.Equal(angebot.Id, notification.AngebotId);
        Assert.Equal(angebot.AngebotNumber, notification.AngebotNumber);
        Assert.Equal("Please fix the VAT rate.", notification.Comment);
        Assert.Equal(OwningInspectorId, notification.InspectorId);
    }

    /// <summary>
    /// Neither aggregate creates or references the other — the handler composes
    /// Angebot.RequestChanges(...) and AngebotReviewComment.Create(...) independently. Proven
    /// here by the fact both repositories receive their own object via their own AddAsync/
    /// mutation call, with only the plain AngebotId value connecting them.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ComposesTheTwoAggregatesIndependently()
    {
        var angebot = SeedAngebotInReview();
        var command = new RequestAngebotChangesCommand(angebot.Id, "Please fix the VAT rate.", AdminId);

        await _handler.HandleAsync(command, CancellationToken.None);

        var comment = Assert.Single(_reviewCommentRepository.AddedComments);
        Assert.Equal(angebot.Id, comment.AngebotId); // only the id links them
        Assert.Empty(_angebotRepository.AddedAngebote); // Angebot was loaded/mutated, never re-added
    }

    // ---- Not found / domain guard ---------------------------------------

    [Fact]
    public async Task HandleAsync_AngebotDoesNotExist_ThrowsNotFoundException()
    {
        var command = new RequestAngebotChangesCommand(AngebotId: 999, "Comment", AdminId);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_AngebotStillDraft_PropagatesDomainGuardFailure_AndCreatesNoComment()
    {
        var angebot = _angebotRepository.Seed(Angebot.Create(1, null, "ANG-2026-00001", OwningInspectorId));
        var command = new RequestAngebotChangesCommand(angebot.Id, "Comment", AdminId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Empty(_reviewCommentRepository.AddedComments);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
        Assert.Empty(_emailSender.AngebotChangesRequestedNotifications);
    }

    // ---- Validation ----------------------------------------------------

    [Theory]
    [InlineData(0, "Comment", 2)]
    [InlineData(1, "", 2)]
    [InlineData(1, "Comment", 0)]
    public async Task HandleAsync_InvalidCommand_ThrowsAndPerformsNoSideEffects(int angebotId, string comment, int reviewedByAdminId)
    {
        var command = new RequestAngebotChangesCommand(angebotId, comment, reviewedByAdminId);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Empty(_reviewCommentRepository.AddedComments);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }
}
