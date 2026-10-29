using FluentValidation;
using RenoTrack.Application.Angebote.Commands.UpdateAngebotItem;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Commands.UpdateAngebotItem;

/// <summary>
/// The line-correction handler had no Application tests of its own before D115; its behaviour was
/// covered at the API layer only. Added with Admin editing because this is the handler where a stale
/// write does the most damage — a <c>PUT</c> replaces every field.
/// </summary>
public class UpdateAngebotItemCommandHandlerTests
{
    private const int OwningInspectorId = 5;
    private const int OtherInspectorId = 6;
    private const int AdminId = 2;

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly OwnershipValidator _ownershipValidator = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly UpdateAngebotItemCommandHandler _handler;

    public UpdateAngebotItemCommandHandlerTests()
    {
        _handler = new UpdateAngebotItemCommandHandler(
            new UpdateAngebotItemCommandValidator(),
            _angebotRepository,
            _ownershipValidator,
            _unitOfWork,
            _auditService);
    }

    private Angebot SeedAngebotWithOneLine(int createdByInspectorId = OwningInspectorId)
    {
        var angebot = _angebotRepository.Seed(Angebot.Create(1, null, "ANG-2026-00001", createdByInspectorId));
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Fliesen", 2m, ItemUnit.SquareMeter(), Money.FromExact(50.00m), VatRate.Standard);
        angebot.AssignChildIds();
        return angebot;
    }

    private static UpdateAngebotItemCommand Command(
        Angebot angebot, int? requestingInspectorId, int performedByUserId, decimal quantity = 3m, string description = "Fliesen, korrigiert") =>
        new(angebot.Id, angebot.Sections[0].Items[0].Id, description, null, "m2", quantity, 50.00m, VatRate.Standard,
            angebot.Version, requestingInspectorId, performedByUserId);

    [Fact]
    public async Task HandleAsync_OwningInspector_CorrectsTheLine_AndIsNotAudited()
    {
        var angebot = SeedAngebotWithOneLine();

        var summary = await _handler.HandleAsync(Command(angebot, OwningInspectorId, OwningInspectorId), CancellationToken.None);

        Assert.Equal(3m, angebot.Sections[0].Items[0].Quantity);
        Assert.Equal(150.00m, summary.NetTotal);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    [Fact]
    public async Task HandleAsync_NonOwningInspector_ThrowsForbidden()
    {
        var angebot = SeedAngebotWithOneLine();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _handler.HandleAsync(Command(angebot, OtherInspectorId, OtherInspectorId), CancellationToken.None));

        Assert.Equal(2m, angebot.Sections[0].Items[0].Quantity);
    }

    [Fact]
    public async Task HandleAsync_Admin_CorrectsAnInspectorsLine_AndIsAudited()
    {
        var angebot = SeedAngebotWithOneLine(createdByInspectorId: 77);
        var section = angebot.Sections[0];
        var item = section.Items[0];

        await _handler.HandleAsync(Command(angebot, null, AdminId), CancellationToken.None);

        Assert.Equal("Fliesen, korrigiert", item.Description);
        Assert.Equal(77, angebot.CreatedByInspectorId);
        var call = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(Angebot), call.EntityType);
        Assert.Equal(AuditAction.AngebotEditedByAdmin, call.Action);
        Assert.Equal(AdminId, call.PerformedByUserId);
        Assert.Equal($"operation=ItemUpdated; sectionId={section.Id}; itemId={item.Id}", call.Details);
    }

    /// <summary>The audit details carry no amount and no text — only the operation and the association.</summary>
    [Fact]
    public async Task HandleAsync_Admin_AuditDetailsCarryNoAmountOrText()
    {
        var angebot = SeedAngebotWithOneLine();

        await _handler.HandleAsync(Command(angebot, null, AdminId, quantity: 7.25m, description: "Geheimer Text"), CancellationToken.None);

        var details = Assert.Single(_auditService.Calls).Details!;
        Assert.DoesNotContain("Geheimer", details);
        Assert.DoesNotContain("7.25", details);
        Assert.DoesNotContain("50", details);
    }

    /// <summary>
    /// The case expectedVersion exists for: an Admin's screen loaded before the Inspector's edit. Their
    /// <c>PUT</c> would otherwise silently revert the newer value.
    /// </summary>
    [Fact]
    public async Task HandleAsync_StaleVersion_CannotOverwriteNewerContent()
    {
        var angebot = SeedAngebotWithOneLine();
        var adminsStaleView = Command(angebot, null, AdminId, quantity: 4m);
        await _handler.HandleAsync(Command(angebot, OwningInspectorId, OwningInspectorId, quantity: 9m), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(adminsStaleView, CancellationToken.None));

        Assert.Equal(9m, angebot.Sections[0].Items[0].Quantity);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    [Fact]
    public async Task HandleAsync_Admin_WhileInReview_IsStillLocked()
    {
        var angebot = SeedAngebotWithOneLine();
        angebot.SubmitForReview();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command(angebot, null, AdminId), CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_InvalidCommand_ThrowsValidationException()
    {
        var angebot = SeedAngebotWithOneLine();
        var command = Command(angebot, null, AdminId) with { ExpectedVersion = -1 };

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }
}
