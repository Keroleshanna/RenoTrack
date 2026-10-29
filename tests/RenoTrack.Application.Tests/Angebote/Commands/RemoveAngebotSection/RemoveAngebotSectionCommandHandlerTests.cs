using FluentValidation;
using RenoTrack.Application.Angebote.Commands.RemoveAngebotSection;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Commands.RemoveAngebotSection;

public class RemoveAngebotSectionCommandHandlerTests
{
    private const int OwningInspectorId = 5;
    private const int OtherInspectorId = 6;
    private const int AdminId = 2;

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly OwnershipValidator _ownershipValidator = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly RemoveAngebotSectionCommandHandler _handler;

    public RemoveAngebotSectionCommandHandlerTests()
    {
        _handler = new RemoveAngebotSectionCommandHandler(
            new RemoveAngebotSectionCommandValidator(),
            _angebotRepository,
            _ownershipValidator,
            _unitOfWork,
            _auditService);
    }

    private Angebot SeedAngebotWithOneSection(int createdByInspectorId = OwningInspectorId)
    {
        var angebot = _angebotRepository.Seed(
            Angebot.Create(leadId: 1, inspectionId: null, "ANG-2026-00001", createdByInspectorId));

        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 2m, ItemUnit.Piece(), Money.FromExact(50.00m), VatRate.Standard);
        angebot.AssignChildIds();

        return angebot;
    }

    private static RemoveAngebotSectionCommand AsInspector(Angebot angebot, int sectionId, int inspectorId = OwningInspectorId) =>
        new(angebot.Id, sectionId, angebot.Version, inspectorId, inspectorId);

    private static RemoveAngebotSectionCommand AsAdmin(Angebot angebot, int sectionId) =>
        new(angebot.Id, sectionId, angebot.Version, RequestingInspectorId: null, PerformedByUserId: AdminId);

    [Fact]
    public async Task HandleAsync_RemovesTheSectionAndReturnsRefreshedTotals()
    {
        var angebot = SeedAngebotWithOneSection();
        var sectionId = angebot.Sections[0].Id;

        var summary = await _handler.HandleAsync(AsInspector(angebot, sectionId), CancellationToken.None);

        Assert.Empty(angebot.Sections);
        Assert.Equal(0m, summary.NetTotal);
        Assert.Equal(0m, summary.GrossTotal);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls); // an Inspector's own edit is not audited
    }

    [Fact]
    public async Task HandleAsync_UnknownAngebot_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            new RemoveAngebotSectionCommand(999, 1, 0, OwningInspectorId, OwningInspectorId), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UnknownSection_ThrowsNotFoundAndSavesNothing()
    {
        var angebot = SeedAngebotWithOneSection();

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(AsInspector(angebot, 9999), CancellationToken.None));

        Assert.Single(angebot.Sections);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    /// <summary>PermissionMatrix.md §3 marks this scoped for an Inspector, so a non-owning Inspector is refused.</summary>
    [Fact]
    public async Task HandleAsync_NonOwningInspector_ThrowsForbiddenAndRemovesNothing()
    {
        var angebot = SeedAngebotWithOneSection();

        await Assert.ThrowsAsync<ForbiddenException>(() => _handler.HandleAsync(
            AsInspector(angebot, angebot.Sections[0].Id, OtherInspectorId),
            CancellationToken.None));

        Assert.Single(angebot.Sections);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    /// <summary>
    /// The edit-lock belongs to the Domain, not the handler: the handler calls straight through and
    /// lets the aggregate throw (CLAUDE.md §6). This proves it is not silently bypassed.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhileInReview_ThrowsAndSavesNothing()
    {
        var angebot = SeedAngebotWithOneSection();
        angebot.SubmitForReview();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(
            AsInspector(angebot, angebot.Sections[0].Id),
            CancellationToken.None));

        Assert.Single(angebot.Sections);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_InvalidCommand_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            new RemoveAngebotSectionCommand(0, 0, -1, 0, 0), CancellationToken.None));
    }

    // ---- Admin editing and freshness (D115) --------------------------------

    [Fact]
    public async Task HandleAsync_Admin_RemovesASectionFromAnInspectorsDraft_AndIsAudited()
    {
        var angebot = SeedAngebotWithOneSection(createdByInspectorId: 77);
        var sectionId = angebot.Sections[0].Id;

        await _handler.HandleAsync(AsAdmin(angebot, sectionId), CancellationToken.None);

        Assert.Empty(angebot.Sections);
        var call = Assert.Single(_auditService.Calls);
        Assert.Equal(AuditAction.AngebotEditedByAdmin, call.Action);
        Assert.Equal(AdminId, call.PerformedByUserId);
        Assert.Equal($"operation=SectionRemoved; sectionId={sectionId}", call.Details);
    }

    [Fact]
    public async Task HandleAsync_StaleExpectedVersion_ThrowsConflict_AndChangesNothing()
    {
        var angebot = SeedAngebotWithOneSection();
        var stale = AsAdmin(angebot, angebot.Sections[0].Id) with { ExpectedVersion = angebot.Version + 5 };

        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(stale, CancellationToken.None));

        Assert.Single(angebot.Sections);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }
}
