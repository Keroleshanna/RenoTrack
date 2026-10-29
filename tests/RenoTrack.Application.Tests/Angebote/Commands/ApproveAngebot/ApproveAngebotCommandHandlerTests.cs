using FluentValidation;
using RenoTrack.Application.Angebote.Commands.ApproveAngebot;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Commands.ApproveAngebot;

public class ApproveAngebotCommandHandlerTests
{
    private const int OwningInspectorId = 5;
    private const int AdminId = 2;

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly ApproveAngebotCommandHandler _handler;

    public ApproveAngebotCommandHandlerTests()
    {
        _handler = new ApproveAngebotCommandHandler(
            new ApproveAngebotCommandValidator(),
            _angebotRepository,
            _unitOfWork,
            _auditService);
    }

    /// <summary>Seeds an Angebot already submitted for review (InReview) — the precondition for Approve.</summary>
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
    public async Task HandleAsync_TransitionsAngebotToApprovedInternally()
    {
        var angebot = SeedAngebotInReview();
        var command = new ApproveAngebotCommand(angebot.Id, angebot.Version, AdminId);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(AngebotStatus.ApprovedInternally, result.Status);
    }

    [Fact]
    public async Task HandleAsync_RecordsReviewedByAdminId()
    {
        var angebot = SeedAngebotInReview();
        var command = new ApproveAngebotCommand(angebot.Id, angebot.Version, AdminId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(AdminId, angebot.ReviewedByAdminId);
    }

    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        var angebot = SeedAngebotInReview();
        var command = new ApproveAngebotCommand(angebot.Id, angebot.Version, AdminId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_LogsAuditAgainstTheAngebot()
    {
        var angebot = SeedAngebotInReview();
        var command = new ApproveAngebotCommand(angebot.Id, angebot.Version, AdminId);

        await _handler.HandleAsync(command, CancellationToken.None);

        var call = Assert.Single(_auditService.Calls);
        Assert.Equal("Angebot", call.EntityType);
        Assert.Equal(angebot.Id, call.EntityId);
        Assert.Equal(AuditAction.AngebotApproved, call.Action);
        Assert.Equal(AdminId, call.PerformedByUserId);
    }

    // ---- Not found / domain guard ---------------------------------------
    // No ownership test here — this command has no ownership concept (PermissionMatrix §4:
    // Admin-"F"); any Admin may approve any Angebot, enforced at the API layer, not here.

    [Fact]
    public async Task HandleAsync_AngebotDoesNotExist_ThrowsNotFoundException()
    {
        var command = new ApproveAngebotCommand(AngebotId: 999, 0, AdminId);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_AngebotStillDraft_PropagatesDomainGuardFailure()
    {
        var angebot = _angebotRepository.Seed(Angebot.Create(1, null, "ANG-2026-00001", OwningInspectorId));
        var command = new ApproveAngebotCommand(angebot.Id, angebot.Version, AdminId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    // ---- Validation ----------------------------------------------------

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    public async Task HandleAsync_InvalidCommand_ThrowsAndPerformsNoSideEffects(int angebotId, int reviewedByAdminId)
    {
        var command = new ApproveAngebotCommand(angebotId, 0, reviewedByAdminId);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    // ---- Freshness (D115) ------------------------------------------------

    /// <summary>
    /// A stale approval must not approve a document the Admin never saw — for example one another
    /// Admin returned for correction, changed and resubmitted meanwhile.
    /// </summary>
    [Fact]
    public async Task HandleAsync_StaleExpectedVersion_ThrowsConflict_AndApprovesNothing()
    {
        var angebot = SeedAngebotInReview();
        var command = new ApproveAngebotCommand(angebot.Id, angebot.Version - 1, AdminId);

        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Equal(AngebotStatus.InReview, angebot.Status);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    [Fact]
    public async Task HandleAsync_NegativeExpectedVersion_ThrowsValidationException()
    {
        var command = new ApproveAngebotCommand(1, -1, AdminId);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    /// <summary>No four-eyes rule (D115): the Admin who corrected an Angebot may approve it.</summary>
    [Fact]
    public async Task HandleAsync_TheAdminWhoCorrectedTheAngebot_MayApproveIt()
    {
        var angebot = SeedAngebotInReview();
        angebot.ReturnForCorrection();
        angebot.UpdateItem(angebot.Sections[0], angebot.Sections[0].Items[0], "Korrigiert", 2m,
            ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);
        angebot.SubmitForReview();

        var result = await _handler.HandleAsync(new ApproveAngebotCommand(angebot.Id, angebot.Version, AdminId), CancellationToken.None);

        Assert.Equal(AngebotStatus.ApprovedInternally, result.Status);
        Assert.Equal(AdminId, angebot.ReviewedByAdminId);
    }
}
