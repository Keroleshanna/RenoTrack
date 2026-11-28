using FluentValidation;
using RenoTrack.Application.Angebote.Commands.AddAngebotSection;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Commands.AddAngebotSection;

public class AddAngebotSectionCommandHandlerTests
{
    private const int OwningInspectorId = 5;
    private const int AdminId = 2;

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly OwnershipValidator _ownershipValidator = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly AddAngebotSectionCommandHandler _handler;

    public AddAngebotSectionCommandHandlerTests()
    {
        _handler = new AddAngebotSectionCommandHandler(
            new AddAngebotSectionCommandValidator(),
            _angebotRepository,
            _ownershipValidator,
            _unitOfWork,
            _auditService);
    }

    private Angebot SeedDraftAngebot(int createdByInspectorId = OwningInspectorId) =>
        _angebotRepository.Seed(Angebot.Create(leadId: 1, inspectionId: null, "ANG-2026-00001", createdByInspectorId));

    /// <summary>Drives a fresh Angebot to InReview (one section, one item, submitted) so tests can exercise the edit-lock.</summary>
    private Angebot SeedAngebotInReview()
    {
        var angebot = SeedDraftAngebot();
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);
        angebot.SubmitForReview();
        return angebot;
    }

    /// <summary>The owning Inspector's command, carrying the Angebot's current version.</summary>
    private static AddAngebotSectionCommand AsInspector(Angebot angebot, string title = "Pos. 1", int sortOrder = 1, int inspectorId = OwningInspectorId) =>
        new(angebot.Id, title, sortOrder, angebot.Version, RequestingInspectorId: inspectorId, PerformedByUserId: inspectorId);

    /// <summary>An Admin's command: unscoped (<c>null</c>), the Admin as actor (D88).</summary>
    private static AddAngebotSectionCommand AsAdmin(Angebot angebot, string title = "Pos. 1", int sortOrder = 1) =>
        new(angebot.Id, title, sortOrder, angebot.Version, RequestingInspectorId: null, PerformedByUserId: AdminId);

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task HandleAsync_ReturnsSectionDtoWithSubmittedValues()
    {
        var angebot = SeedDraftAngebot();

        var result = await _handler.HandleAsync(AsInspector(angebot, "Pos. 1 Baustelleneinrichtung"), CancellationToken.None);

        Assert.Equal("Pos. 1 Baustelleneinrichtung", result.Title);
        Assert.Equal(1, result.SortOrder);
        Assert.Equal(0m, result.Subtotal);
    }

    [Fact]
    public async Task HandleAsync_AddsSectionToTheAngebot()
    {
        var angebot = SeedDraftAngebot();

        await _handler.HandleAsync(AsInspector(angebot), CancellationToken.None);

        Assert.Single(angebot.Sections);
    }

    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        var angebot = SeedDraftAngebot();

        await _handler.HandleAsync(AsInspector(angebot), CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_FromChangesRequested_TransitionsAngebotBackToDraft()
    {
        var angebot = SeedAngebotInReview();
        angebot.RequestChanges(reviewedByAdminId: 2);
        Assert.Equal(AngebotStatus.ChangesRequested, angebot.Status); // sanity check on the fixture

        await _handler.HandleAsync(AsInspector(angebot, "New section after feedback", 2), CancellationToken.None);

        Assert.Equal(AngebotStatus.Draft, angebot.Status);
    }

    // ---- Not found / ownership / state guard ------------------------------

    [Fact]
    public async Task HandleAsync_AngebotDoesNotExist_ThrowsNotFoundException()
    {
        var command = new AddAngebotSectionCommand(AngebotId: 999, "Pos. 1", 1, 0, OwningInspectorId, OwningInspectorId);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_InspectorDoesNotOwnAngebot_ThrowsForbiddenException()
    {
        var angebot = SeedDraftAngebot();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _handler.HandleAsync(AsInspector(angebot, inspectorId: 999), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_AngebotInReview_PropagatesDomainGuardFailure()
    {
        var angebot = SeedAngebotInReview(); // now InReview — locked from editing

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(AsInspector(angebot, "Too late", 2), CancellationToken.None));
    }

    // ---- Admin editing (D115) ----------------------------------------------

    [Fact]
    public async Task HandleAsync_Admin_EditsAnInspectorsDraftWithoutAnOwnershipCheck()
    {
        var angebot = SeedDraftAngebot(createdByInspectorId: 77);

        await _handler.HandleAsync(AsAdmin(angebot), CancellationToken.None);

        Assert.Single(angebot.Sections);
        Assert.Equal(77, angebot.CreatedByInspectorId); // authorship never moves
    }

    [Fact]
    public async Task HandleAsync_Admin_IsAuditedAgainstTheAngebot_AfterTheSave()
    {
        var angebot = SeedDraftAngebot();

        var section = await _handler.HandleAsync(AsAdmin(angebot), CancellationToken.None);

        var call = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(Angebot), call.EntityType);
        Assert.Equal(angebot.Id, call.EntityId);
        Assert.Equal(AuditAction.AngebotEditedByAdmin, call.Action);
        Assert.Equal(AdminId, call.PerformedByUserId);
        Assert.Equal($"operation=SectionAdded; sectionId={section.Id}", call.Details);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_Inspector_IsNotAudited()
    {
        var angebot = SeedDraftAngebot();

        await _handler.HandleAsync(AsInspector(angebot), CancellationToken.None);

        Assert.Empty(_auditService.Calls);
    }

    [Fact]
    public async Task HandleAsync_Admin_AngebotInReview_IsStillLocked()
    {
        var angebot = SeedAngebotInReview();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(AsAdmin(angebot), CancellationToken.None));
        Assert.Empty(_auditService.Calls);
    }

    // ---- Freshness (D115) ------------------------------------------------

    [Fact]
    public async Task HandleAsync_StaleExpectedVersion_ThrowsConflict_AndChangesNothing()
    {
        var angebot = SeedDraftAngebot();
        var stale = AsAdmin(angebot) with { ExpectedVersion = angebot.Version + 1 };

        var exception = await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(stale, CancellationToken.None));

        Assert.Equal(ConflictException.ConcurrentChangeMessage, exception.Message);
        Assert.Empty(angebot.Sections);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    [Fact]
    public async Task HandleAsync_NonOwningInspectorWithAStaleVersion_IsForbiddenFirst()
    {
        var angebot = SeedDraftAngebot();
        var command = AsInspector(angebot, inspectorId: 999) with { ExpectedVersion = 42 };

        await Assert.ThrowsAsync<ForbiddenException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    // ---- Validation ----------------------------------------------------

    [Theory]
    [InlineData(0, "Pos. 1", 0, 5, 5)]
    [InlineData(1, "", 0, 5, 5)]
    [InlineData(1, "Pos. 1", -1, 5, 5)]
    [InlineData(1, "Pos. 1", 0, 0, 5)]
    [InlineData(1, "Pos. 1", 0, 5, 0)]
    public async Task HandleAsync_InvalidCommand_ThrowsAndPerformsNoSideEffects(
        int angebotId, string title, int expectedVersion, int requestingInspectorId, int performedByUserId)
    {
        var command = new AddAngebotSectionCommand(angebotId, title, 1, expectedVersion, requestingInspectorId, performedByUserId);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }
}
