using FluentValidation;
using RenoTrack.Application.Angebote.Commands.RemoveAngebotItem;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Commands.RemoveAngebotItem;

public class RemoveAngebotItemCommandHandlerTests
{
    private const int OwningInspectorId = 5;
    private const int OtherInspectorId = 6;

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly OwnershipValidator _ownershipValidator = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly RemoveAngebotItemCommandHandler _handler;

    public RemoveAngebotItemCommandHandlerTests()
    {
        _handler = new RemoveAngebotItemCommandHandler(
            new RemoveAngebotItemCommandValidator(),
            _angebotRepository,
            _ownershipValidator,
            _unitOfWork);
    }

    /// <summary>Two sections, one item each, so "resolved the owning section" is a real assertion.</summary>
    private Angebot SeedAngebotWithTwoSections(int createdByInspectorId = OwningInspectorId)
    {
        var angebot = _angebotRepository.Seed(
            Angebot.Create(leadId: 1, inspectionId: null, "ANG-2026-00001", createdByInspectorId));

        var first = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(first, "Keep", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);

        var second = angebot.AddSection("Pos. 2", 2);
        angebot.AddItemToSection(second, "Drop", 1m, ItemUnit.Piece(), Money.FromExact(90.00m), VatRate.Standard);

        angebot.AssignChildIds();
        return angebot;
    }

    [Fact]
    public async Task HandleAsync_RemovesOnlyThatItemAndReturnsRefreshedTotals()
    {
        var angebot = SeedAngebotWithTwoSections();
        var doomed = angebot.Sections[1].Items[0].Id;

        var summary = await _handler.HandleAsync(
            new RemoveAngebotItemCommand(angebot.Id, doomed, OwningInspectorId),
            CancellationToken.None);

        Assert.Equal(2, angebot.Sections.Count);
        Assert.Single(angebot.Sections[0].Items);
        Assert.Empty(angebot.Sections[1].Items);
        Assert.Equal(10.00m, summary.NetTotal);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_UnknownItem_ThrowsNotFoundAndSavesNothing()
    {
        var angebot = SeedAngebotWithTwoSections();

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            new RemoveAngebotItemCommand(angebot.Id, 9999, OwningInspectorId), CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_NonOwningInspector_ThrowsForbidden()
    {
        var angebot = SeedAngebotWithTwoSections();

        await Assert.ThrowsAsync<ForbiddenException>(() => _handler.HandleAsync(
            new RemoveAngebotItemCommand(angebot.Id, angebot.Sections[0].Items[0].Id, OtherInspectorId),
            CancellationToken.None));

        Assert.Single(angebot.Sections[0].Items);
    }

    [Fact]
    public async Task HandleAsync_WhileInReview_ThrowsAndSavesNothing()
    {
        var angebot = SeedAngebotWithTwoSections();
        angebot.SubmitForReview();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(
            new RemoveAngebotItemCommand(angebot.Id, angebot.Sections[0].Items[0].Id, OwningInspectorId),
            CancellationToken.None));

        Assert.Single(angebot.Sections[0].Items);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_InvalidCommand_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            new RemoveAngebotItemCommand(0, 0, 0), CancellationToken.None));
    }
}
