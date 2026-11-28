using FluentValidation;
using RenoTrack.Application.Angebote.Commands.ReturnAngebotForCorrection;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Commands.ReturnAngebotForCorrection;

/// <summary>"Zur Korrektur zurückholen" — <c>InReview → Draft</c>, Admin only (D115).</summary>
public class ReturnAngebotForCorrectionCommandHandlerTests
{
    private const int OwningInspectorId = 5;
    private const int AdminId = 2;

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly ReturnAngebotForCorrectionCommandHandler _handler;

    public ReturnAngebotForCorrectionCommandHandlerTests()
    {
        _handler = new ReturnAngebotForCorrectionCommandHandler(
            new ReturnAngebotForCorrectionCommandValidator(),
            _angebotRepository,
            _unitOfWork,
            _auditService);
    }

    private Angebot SeedAngebotInReview()
    {
        var angebot = _angebotRepository.Seed(Angebot.Create(1, null, "ANG-2026-00001", OwningInspectorId));
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);
        angebot.SubmitForReview();
        return angebot;
    }

    [Fact]
    public async Task HandleAsync_ReturnsTheAngebotToDraft()
    {
        var angebot = SeedAngebotInReview();

        var result = await _handler.HandleAsync(
            new ReturnAngebotForCorrectionCommand(angebot.Id, angebot.Version, AdminId), CancellationToken.None);

        Assert.Equal(AngebotStatus.Draft, result.Status);
        Assert.Equal(AngebotStatus.Draft, angebot.Status);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_KeepsAuthorshipAndRecordsNoReviewer()
    {
        var angebot = SeedAngebotInReview();

        await _handler.HandleAsync(new ReturnAngebotForCorrectionCommand(angebot.Id, angebot.Version, AdminId), CancellationToken.None);

        Assert.Equal(OwningInspectorId, angebot.CreatedByInspectorId);
        Assert.Null(angebot.ReviewedByAdminId);
    }

    [Fact]
    public async Task HandleAsync_IsAuditedAgainstTheAngebot_WithTheAdminAsActor_AndNoDetails()
    {
        var angebot = SeedAngebotInReview();

        await _handler.HandleAsync(new ReturnAngebotForCorrectionCommand(angebot.Id, angebot.Version, AdminId), CancellationToken.None);

        var call = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(Angebot), call.EntityType);
        Assert.Equal(angebot.Id, call.EntityId);
        Assert.Equal(AuditAction.AngebotReturnedForCorrection, call.Action);
        Assert.Equal(AdminId, call.PerformedByUserId);
        Assert.Null(call.Details);
    }

    [Theory]
    [InlineData(AngebotStatus.Draft)]
    [InlineData(AngebotStatus.ChangesRequested)]
    [InlineData(AngebotStatus.ApprovedInternally)]
    [InlineData(AngebotStatus.Sent)]
    public async Task HandleAsync_NotInReview_PropagatesDomainGuardFailure_AndAuditsNothing(AngebotStatus status)
    {
        var angebot = SeedAngebotInReview();
        switch (status)
        {
            case AngebotStatus.Draft:
                angebot.ReturnForCorrection();
                break;
            case AngebotStatus.ChangesRequested:
                angebot.RequestChanges(AdminId);
                break;
            case AngebotStatus.ApprovedInternally:
                angebot.Approve(AdminId);
                break;
            case AngebotStatus.Sent:
                angebot.Approve(AdminId);
                angebot.Send();
                break;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(
            new ReturnAngebotForCorrectionCommand(angebot.Id, angebot.Version, AdminId), CancellationToken.None));

        Assert.Equal(status, angebot.Status);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    [Fact]
    public async Task HandleAsync_StaleExpectedVersion_ThrowsConflict_AndChangesNothing()
    {
        var angebot = SeedAngebotInReview();

        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(
            new ReturnAngebotForCorrectionCommand(angebot.Id, angebot.Version - 1, AdminId), CancellationToken.None));

        Assert.Equal(AngebotStatus.InReview, angebot.Status);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    [Fact]
    public async Task HandleAsync_UnknownAngebot_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            new ReturnAngebotForCorrectionCommand(999, 0, AdminId), CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 0, 2)]
    [InlineData(1, -1, 2)]
    [InlineData(1, 0, 0)]
    public async Task HandleAsync_InvalidCommand_ThrowsAndPerformsNoSideEffects(int angebotId, int expectedVersion, int adminId)
    {
        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            new ReturnAngebotForCorrectionCommand(angebotId, expectedVersion, adminId), CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }
}
