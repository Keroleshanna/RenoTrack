using FluentValidation;

namespace RenoTrack.Application.Angebote.Commands.AddAngebotItem;

public sealed class AddAngebotItemCommandValidator : AbstractValidator<AddAngebotItemCommand>
{
    public AddAngebotItemCommandValidator()
    {
        RuleFor(c => c.AngebotId).GreaterThan(0);
        RuleFor(c => c.SectionId).GreaterThan(0);
        RuleFor(c => c.InspectorId).GreaterThan(0);
        RuleFor(c => c.Quantity).GreaterThan(0);
        RuleFor(c => c.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(c => c.VatRate).IsInEnum();
        RuleFor(c => c.CatalogItemId).GreaterThan(0).When(c => c.CatalogItemId is not null);

        // Custom path only — the Catalog path derives these from the loaded CatalogItem instead.
        When(c => c.CatalogItemId is null, () =>
        {
            RuleFor(c => c.Description).NotEmpty();
            RuleFor(c => c.UnitCode).NotEmpty();
        });
    }
}
