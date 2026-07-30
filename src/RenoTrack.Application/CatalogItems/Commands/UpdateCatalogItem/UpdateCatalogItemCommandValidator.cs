using FluentValidation;

namespace RenoTrack.Application.CatalogItems.Commands.UpdateCatalogItem;

public sealed class UpdateCatalogItemCommandValidator : AbstractValidator<UpdateCatalogItemCommand>
{
    public UpdateCatalogItemCommandValidator()
    {
        RuleFor(c => c.Id).GreaterThan(0);
        RuleFor(c => c.Title).NotEmpty();
        RuleFor(c => c.DefaultUnitCode).NotEmpty();
        RuleFor(c => c.SuggestedUnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(c => c.UpdatedByAdminUserId).GreaterThan(0);
    }
}
