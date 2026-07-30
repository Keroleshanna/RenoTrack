using FluentValidation;

namespace RenoTrack.Application.CatalogItems.Commands.CreateCatalogItem;

public sealed class CreateCatalogItemCommandValidator : AbstractValidator<CreateCatalogItemCommand>
{
    public CreateCatalogItemCommandValidator()
    {
        RuleFor(c => c.Title).NotEmpty();
        RuleFor(c => c.DefaultUnitCode).NotEmpty();
        RuleFor(c => c.SuggestedUnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(c => c.CreatedByAdminUserId).GreaterThan(0);
    }
}
