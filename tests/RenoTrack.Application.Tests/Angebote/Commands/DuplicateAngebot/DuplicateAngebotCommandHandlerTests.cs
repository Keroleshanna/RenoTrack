using FluentValidation;
using RenoTrack.Application.Angebote.Commands.DuplicateAngebot;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Commands.DuplicateAngebot;

/// <summary>
/// SRS FR-4.11. The copy must be a genuinely new Draft — fresh number, no reviewer, no timestamps —
/// carrying the section/item tree and nothing that belonged to the source's own workflow.
/// </summary>
public class DuplicateAngebotCommandHandlerTests
{
    private const int OwningInspectorId = 5;
    private const int OtherInspectorId = 6;

    private readonly FakeLeadRepository _leadRepository = new();
    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly FakeNumberGeneratorService _numberGenerator = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly DuplicateAngebotCommandHandler _handler;

    public DuplicateAngebotCommandHandlerTests()
    {
        _handler = new DuplicateAngebotCommandHandler(
            new DuplicateAngebotCommandValidator(),
            _leadRepository,
            _angebotRepository,
            _numberGenerator,
            new OwnershipValidator(),
            _unitOfWork,
            _auditService);
    }

    /// <summary>A Lead that has had its Inspection done, which is MarkAngebotInProgress's precondition.</summary>
    private Lead SeedTargetLead(int assignedInspectorId = OwningInspectorId)
    {
        var lead = _leadRepository.Seed(
            Lead.Create("Target", "0176 0000001", "target@example.com", LeadSource.Phone));

        lead.AssignInspector(assignedInspectorId);
        lead.MarkInspectionScheduled();
        lead.MarkInspectionDone();

        return lead;
    }

    /// <summary>
    /// Two sections with two items, one of which came from the Catalog, so the copy's fidelity and
    /// the CatalogItemId decision are both observable.
    /// </summary>
    private Angebot SeedSourceAngebot(int createdByInspectorId = OwningInspectorId)
    {
        var angebot = _angebotRepository.Seed(
            Angebot.Create(leadId: 99, inspectionId: 7, "ANG-2026-00001", createdByInspectorId));

        var second = angebot.AddSection("Pos. 2 Fliesen", 2);
        var first = angebot.AddSection("Pos. 1 Abbruch", 1);

        angebot.AddItemToSection(first, "Wände abbrechen", 10m, ItemUnit.SquareMeter(),
            Money.FromExact(25.00m), VatRate.Standard, "Ziegelwand");

        angebot.AddItemToSection(second, "Fliesen verlegen", 13.5m, ItemUnit.SquareMeter(),
            Money.FromExact(82.25m), VatRate.Standard, "Feinsteinzeug", catalogItemId: 42);

        angebot.AssignChildIds();
        return angebot;
    }

    // ---- Happy path --------------------------------------------------------

    /// <summary>
    /// The number must come from <c>INumberGeneratorService</c>, not from the source. The fake is
    /// pointed at a distinct value first, so this cannot pass by the two happening to coincide —
    /// which is exactly how it failed when written.
    /// </summary>
    [Fact]
    public async Task HandleAsync_CreatesANewDraftOnTheTargetLeadWithAFreshNumber()
    {
        var lead = SeedTargetLead();
        var source = SeedSourceAngebot();
        _numberGenerator.NextAngebotNumber = "ANG-2026-09999";

        var result = await _handler.HandleAsync(
            new DuplicateAngebotCommand(source.Id, lead.Id, OwningInspectorId), CancellationToken.None);

        Assert.Equal(lead.Id, result.LeadId);
        Assert.Equal(AngebotStatus.Draft, result.Status);
        Assert.Equal("ANG-2026-09999", result.AngebotNumber);
        Assert.NotEqual(source.AngebotNumber, result.AngebotNumber);
        Assert.Equal(DateTime.UtcNow.Year, Assert.Single(_numberGenerator.RequestedYears));
        Assert.Null(result.ReviewedByAdminId);
        Assert.Null(result.SentAt);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    /// <summary>
    /// The source's InspectionId belongs to the source's Lead — carrying it over would attach the
    /// new Angebot to another Lead's site visit.
    /// </summary>
    [Fact]
    public async Task HandleAsync_DoesNotCarryOverTheSourcesInspection()
    {
        var lead = SeedTargetLead();
        var source = SeedSourceAngebot();
        Assert.NotNull(source.InspectionId);

        var result = await _handler.HandleAsync(
            new DuplicateAngebotCommand(source.Id, lead.Id, OwningInspectorId), CancellationToken.None);

        Assert.Null(result.InspectionId);
    }

    [Fact]
    public async Task HandleAsync_CopiesEverySectionAndItemWithTotalsRecalculated()
    {
        var lead = SeedTargetLead();
        var source = SeedSourceAngebot();

        await _handler.HandleAsync(
            new DuplicateAngebotCommand(source.Id, lead.Id, OwningInspectorId), CancellationToken.None);

        var duplicate = Assert.Single(_angebotRepository.AddedAngebote);

        Assert.Equal(2, duplicate.Sections.Count);
        Assert.Equal(["Pos. 1 Abbruch", "Pos. 2 Fliesen"], duplicate.Sections.Select(s => s.Title));

        // 10 × 25.00 + 13.5 × 82.25 = 250.00 + 1110.375 → BR-11 rounds the line to 1110.38.
        Assert.Equal(source.NetTotal, duplicate.NetTotal);
        Assert.Equal(source.GrossTotal, duplicate.GrossTotal);
    }

    /// <summary>
    /// BR-8 makes <c>CatalogItemId</c> a traceability link only — every item holds its own copy of
    /// description, specification, unit and price — so the copy depends on nothing in the Catalog's
    /// current state. BR-12 means the row is never deleted and BR-14 keeps a retired item a valid
    /// reference, so preserving the link cannot dangle or misbehave. Dropping it would erase a true
    /// fact about the line's origin.
    /// </summary>
    [Fact]
    public async Task HandleAsync_PreservesTheCatalogItemTraceabilityLink()
    {
        var lead = SeedTargetLead();
        var source = SeedSourceAngebot();

        await _handler.HandleAsync(
            new DuplicateAngebotCommand(source.Id, lead.Id, OwningInspectorId), CancellationToken.None);

        var duplicate = Assert.Single(_angebotRepository.AddedAngebote);
        var items = duplicate.Sections.SelectMany(s => s.Items).ToList();

        Assert.Equal(42, Assert.Single(items, i => i.Description == "Fliesen verlegen").CatalogItemId);
        Assert.Null(Assert.Single(items, i => i.Description == "Wände abbrechen").CatalogItemId);
    }

    [Fact]
    public async Task HandleAsync_CopiesItemFieldsFaithfully()
    {
        var lead = SeedTargetLead();
        var source = SeedSourceAngebot();

        await _handler.HandleAsync(
            new DuplicateAngebotCommand(source.Id, lead.Id, OwningInspectorId), CancellationToken.None);

        var duplicate = Assert.Single(_angebotRepository.AddedAngebote);
        var item = Assert.Single(duplicate.Sections.SelectMany(s => s.Items), i => i.Description == "Fliesen verlegen");

        Assert.Equal("Feinsteinzeug", item.Specification);
        Assert.Equal(13.5m, item.Quantity);
        Assert.Equal("m2", item.Unit.Code);
        Assert.Equal(Money.FromExact(82.25m), item.UnitPrice);
        Assert.Equal(VatRate.Standard, item.VatRate);
    }

    [Fact]
    public async Task HandleAsync_LeavesTheSourceUntouched()
    {
        var lead = SeedTargetLead();
        var source = SeedSourceAngebot();

        await _handler.HandleAsync(
            new DuplicateAngebotCommand(source.Id, lead.Id, OwningInspectorId), CancellationToken.None);

        Assert.Equal(2, source.Sections.Count);
        Assert.Equal(99, source.LeadId);
        Assert.Equal("ANG-2026-00001", source.AngebotNumber);
    }

    [Fact]
    public async Task HandleAsync_MovesTheTargetLeadToAngebotInProgressAndAuditsAgainstIt()
    {
        var lead = SeedTargetLead();
        var source = SeedSourceAngebot();

        await _handler.HandleAsync(
            new DuplicateAngebotCommand(source.Id, lead.Id, OwningInspectorId), CancellationToken.None);

        Assert.Equal(LeadStatus.AngebotInProgress, lead.Status);

        var entry = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(Lead), entry.EntityType);
        Assert.Equal(lead.Id, entry.EntityId);
        Assert.Equal(AuditAction.AngebotCreated, entry.Action);
    }

    [Fact]
    public async Task HandleAsync_CopiesAnEmptyAngebotWithoutError()
    {
        var lead = SeedTargetLead();
        var source = _angebotRepository.Seed(
            Angebot.Create(leadId: 99, inspectionId: null, "ANG-2026-00002", OwningInspectorId));

        var result = await _handler.HandleAsync(
            new DuplicateAngebotCommand(source.Id, lead.Id, OwningInspectorId), CancellationToken.None);

        Assert.Equal(0m, result.NetTotal);
        Assert.Empty(Assert.Single(_angebotRepository.AddedAngebote).Sections);
    }

    // ---- Guards ------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_UnknownSource_ThrowsNotFound()
    {
        var lead = SeedTargetLead();

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            new DuplicateAngebotCommand(999, lead.Id, OwningInspectorId), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UnknownTargetLead_ThrowsNotFound()
    {
        var source = SeedSourceAngebot();

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            new DuplicateAngebotCommand(source.Id, 999, OwningInspectorId), CancellationToken.None));
    }

    /// <summary>Source scope: only the Inspector's own Angebote (PermissionMatrix.md §3, v1 default).</summary>
    [Fact]
    public async Task HandleAsync_SourceOwnedByAnotherInspector_ThrowsForbidden()
    {
        var lead = SeedTargetLead();
        var source = SeedSourceAngebot(createdByInspectorId: OtherInspectorId);

        await Assert.ThrowsAsync<ForbiddenException>(() => _handler.HandleAsync(
            new DuplicateAngebotCommand(source.Id, lead.Id, OwningInspectorId), CancellationToken.None));

        Assert.Empty(_angebotRepository.AddedAngebote);
    }

    [Fact]
    public async Task HandleAsync_TargetLeadOwnedByAnotherInspector_ThrowsForbidden()
    {
        var lead = SeedTargetLead(assignedInspectorId: OtherInspectorId);
        var source = SeedSourceAngebot();

        await Assert.ThrowsAsync<ForbiddenException>(() => _handler.HandleAsync(
            new DuplicateAngebotCommand(source.Id, lead.Id, OwningInspectorId), CancellationToken.None));

        Assert.Empty(_angebotRepository.AddedAngebote);
    }

    /// <summary>
    /// StateMachine.md §2.4 — duplicating must not become a second route around "one non-terminal
    /// Angebot per Lead".
    /// </summary>
    [Fact]
    public async Task HandleAsync_TargetLeadAlreadyHasAnActiveAngebot_ThrowsConflict()
    {
        var lead = SeedTargetLead();
        var source = SeedSourceAngebot();
        _angebotRepository.HasActiveAngebotForLead = true;

        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(
            new DuplicateAngebotCommand(source.Id, lead.Id, OwningInspectorId), CancellationToken.None));

        Assert.Empty(_angebotRepository.AddedAngebote);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    /// <summary>The Lead's own guard: an Angebot cannot start before the Inspection is done.</summary>
    [Fact]
    public async Task HandleAsync_TargetLeadNotReady_Throws()
    {
        var lead = _leadRepository.Seed(
            Lead.Create("Too early", "0176 0000002", "early@example.com", LeadSource.Phone));
        lead.AssignInspector(OwningInspectorId);

        var source = SeedSourceAngebot();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(
            new DuplicateAngebotCommand(source.Id, lead.Id, OwningInspectorId), CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_InvalidCommand_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            new DuplicateAngebotCommand(0, 0, 0), CancellationToken.None));
    }
}
