using FluentValidation;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Angebote.Commands.UpdateAngebotItem;

/// <summary>
/// Corrects an existing line on an editable Angebot (`PermissionMatrix.md` §3, Phase 10).
/// </summary>
/// <remarks>
/// <para>
/// <b>No <c>CatalogItemId</c> parameter, unlike <c>AddAngebotItemCommand</c>.</b> Adding has two
/// modes (FR-4.9: from the Catalog, or hand-written) because it decides where the values come
/// from. Editing has one: the caller supplies the values outright. Re-pointing a line at a
/// different Catalog entry is not a correction — it is a different line, and add+remove already
/// expresses it.
/// </para>
/// <para>
/// Consequently <c>Description</c> and <c>UnitCode</c> are non-nullable here where the add command
/// makes them optional: there is no Catalog entry to fall back on.
/// </para>
/// <para>
/// Owning Inspector or any Admin (D115): <c>RequestingInspectorId</c> is the scope (<c>null</c> for
/// an Admin) and <c>PerformedByUserId</c> the actor (D88). <c>ExpectedVersion</c> matters most
/// here: a <c>PUT</c> replaces every field, so a stale one would silently revert a newer edit.
/// </para>
/// </remarks>
public sealed record UpdateAngebotItemCommand(
    int AngebotId,
    int ItemId,
    string Description,
    string? Specification,
    string UnitCode,
    decimal Quantity,
    decimal UnitPrice,
    VatRate VatRate,
    int ExpectedVersion,
    int? RequestingInspectorId,
    int PerformedByUserId);

/// <summary>
/// Shape only (CLAUDE.md §5). The edit-lock (`StateMachine.md` §2.4) and the value guards are the
/// aggregate's, and are not duplicated here.
/// </summary>
public sealed class UpdateAngebotItemCommandValidator : AbstractValidator<UpdateAngebotItemCommand>
{
    public UpdateAngebotItemCommandValidator()
    {
        RuleFor(c => c.AngebotId).GreaterThan(0);
        RuleFor(c => c.ItemId).GreaterThan(0);
        RuleFor(c => c.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(c => c.RequestingInspectorId).GreaterThan(0).When(c => c.RequestingInspectorId is not null);
        RuleFor(c => c.PerformedByUserId).GreaterThan(0);
        RuleFor(c => c.Description).NotEmpty();
        RuleFor(c => c.UnitCode).NotEmpty();
        RuleFor(c => c.Quantity).GreaterThan(0);
        RuleFor(c => c.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(c => c.VatRate).IsInEnum();
    }
}
