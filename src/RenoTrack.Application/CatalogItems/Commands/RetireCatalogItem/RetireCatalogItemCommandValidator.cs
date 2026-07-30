using FluentValidation;

namespace RenoTrack.Application.CatalogItems.Commands.RetireCatalogItem;

public sealed class RetireCatalogItemCommandValidator : AbstractValidator<RetireCatalogItemCommand>
{
    public RetireCatalogItemCommandValidator()
    {
        RuleFor(c => c.Id).GreaterThan(0);
        RuleFor(c => c.RetiredByAdminUserId).GreaterThan(0);
    }
}
