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

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly OwnershipValidator _ownershipValidator = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly AddAngebotSectionCommandHandler _handler;

    public AddAngebotSectionCommandHandlerTests()
    {
        _handler = new AddAngebotSectionCommandHandler(
            new AddAngebotSectionCommandValidator(),
            _angebotRepository,
            _ownershipValidator,
            _unitOfWork);
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

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task HandleAsync_ReturnsSectionDtoWithSubmittedValues()
    {
        var angebot = SeedDraftAngebot();
        var command = new AddAngebotSectionCommand(angebot.Id, "Pos. 1 Baustelleneinrichtung", 1, OwningInspectorId);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal("Pos. 1 Baustelleneinrichtung", result.Title);
        Assert.Equal(1, result.SortOrder);
        Assert.Equal(0m, result.Subtotal);
    }

    [Fact]
    public async Task HandleAsync_AddsSectionToTheAngebot()
    {
        var angebot = SeedDraftAngebot();
        var command = new AddAngebotSectionCommand(angebot.Id, "Pos. 1", 1, OwningInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Single(angebot.Sections);
    }

    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        var angebot = SeedDraftAngebot();
        var command = new AddAngebotSectionCommand(angebot.Id, "Pos. 1", 1, OwningInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_FromChangesRequested_TransitionsAngebotBackToDraft()
    {
        var angebot = SeedAngebotInReview();
        angebot.RequestChanges(reviewedByAdminId: 2);
        Assert.Equal(AngebotStatus.ChangesRequested, angebot.Status); // sanity check on the fixture
        var command = new AddAngebotSectionCommand(angebot.Id, "New section after feedback", 2, OwningInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(AngebotStatus.Draft, angebot.Status);
    }

    // ---- Not found / ownership / state guard ------------------------------

    [Fact]
    public async Task HandleAsync_AngebotDoesNotExist_ThrowsNotFoundException()
    {
        var command = new AddAngebotSectionCommand(AngebotId: 999, "Pos. 1", 1, OwningInspectorId);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_InspectorDoesNotOwnAngebot_ThrowsForbiddenException()
    {
        var angebot = SeedDraftAngebot();
        var command = new AddAngebotSectionCommand(angebot.Id, "Pos. 1", 1, InspectorId: 999);

        await Assert.ThrowsAsync<ForbiddenException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_AngebotInReview_PropagatesDomainGuardFailure()
    {
        var angebot = SeedAngebotInReview(); // now InReview — locked from editing
        var command = new AddAngebotSectionCommand(angebot.Id, "Too late", 2, OwningInspectorId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    // ---- Validation ----------------------------------------------------

    [Theory]
    [InlineData(0, "Pos. 1", 5)]
    [InlineData(1, "", 5)]
    [InlineData(1, "Pos. 1", 0)]
    public async Task HandleAsync_InvalidCommand_ThrowsAndPerformsNoSideEffects(int angebotId, string title, int inspectorId)
    {
        var command = new AddAngebotSectionCommand(angebotId, title, 1, inspectorId);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }
}
