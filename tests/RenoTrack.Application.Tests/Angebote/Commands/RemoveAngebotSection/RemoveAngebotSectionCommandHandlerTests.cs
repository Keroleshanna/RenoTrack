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

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly OwnershipValidator _ownershipValidator = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly RemoveAngebotSectionCommandHandler _handler;

    public RemoveAngebotSectionCommandHandlerTests()
    {
        _handler = new RemoveAngebotSectionCommandHandler(
            new RemoveAngebotSectionCommandValidator(),
            _angebotRepository,
            _ownershipValidator,
            _unitOfWork);
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

    [Fact]
    public async Task HandleAsync_RemovesTheSectionAndReturnsRefreshedTotals()
    {
        var angebot = SeedAngebotWithOneSection();
        var sectionId = angebot.Sections[0].Id;

        var summary = await _handler.HandleAsync(
            new RemoveAngebotSectionCommand(angebot.Id, sectionId, OwningInspectorId),
            CancellationToken.None);

        Assert.Empty(angebot.Sections);
        Assert.Equal(0m, summary.NetTotal);
        Assert.Equal(0m, summary.GrossTotal);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_UnknownAngebot_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            new RemoveAngebotSectionCommand(999, 1, OwningInspectorId), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UnknownSection_ThrowsNotFoundAndSavesNothing()
    {
        var angebot = SeedAngebotWithOneSection();

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            new RemoveAngebotSectionCommand(angebot.Id, 9999, OwningInspectorId), CancellationToken.None));

        Assert.Single(angebot.Sections);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    /// <summary>PermissionMatrix.md §3 marks this scoped, so a non-owning Inspector is refused.</summary>
    [Fact]
    public async Task HandleAsync_NonOwningInspector_ThrowsForbiddenAndRemovesNothing()
    {
        var angebot = SeedAngebotWithOneSection();

        await Assert.ThrowsAsync<ForbiddenException>(() => _handler.HandleAsync(
            new RemoveAngebotSectionCommand(angebot.Id, angebot.Sections[0].Id, OtherInspectorId),
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
            new RemoveAngebotSectionCommand(angebot.Id, angebot.Sections[0].Id, OwningInspectorId),
            CancellationToken.None));

        Assert.Single(angebot.Sections);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_InvalidCommand_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            new RemoveAngebotSectionCommand(0, 0, 0), CancellationToken.None));
    }
}
