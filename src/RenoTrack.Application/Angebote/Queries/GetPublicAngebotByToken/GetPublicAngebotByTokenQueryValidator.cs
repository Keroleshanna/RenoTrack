using FluentValidation;

namespace RenoTrack.Application.Angebote.Queries.GetPublicAngebotByToken;

/// <summary>
/// Shape only (CLAUDE.md §5). Deliberately just "present" — no length or character-class rule.
///
/// A format rule here would be a second, quieter definition of what a token looks like, competing
/// with the generator's; changing the token length later would then silently start rejecting every
/// previously issued link. The repository lookup is an exact match on an indexed column, so an
/// unknown or malformed token costs one point query and returns the same 404 either way — there is
/// nothing for a pre-filter to protect.
/// </summary>
public sealed class GetPublicAngebotByTokenQueryValidator : AbstractValidator<GetPublicAngebotByTokenQuery>
{
    public GetPublicAngebotByTokenQueryValidator()
    {
        RuleFor(q => q.Token).NotEmpty();
    }
}
