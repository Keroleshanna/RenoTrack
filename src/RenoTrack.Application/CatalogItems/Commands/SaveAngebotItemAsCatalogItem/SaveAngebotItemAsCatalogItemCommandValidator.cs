using FluentValidation;

namespace RenoTrack.Application.CatalogItems.Commands.SaveAngebotItemAsCatalogItem;

public sealed class SaveAngebotItemAsCatalogItemCommandValidator
    : AbstractValidator<SaveAngebotItemAsCatalogItemCommand>
{
    public SaveAngebotItemAsCatalogItemCommandValidator()
    {
        RuleFor(c => c.AngebotItemId).GreaterThan(0);
        RuleFor(c => c.SavedByInspectorId).GreaterThan(0);
    }
}
