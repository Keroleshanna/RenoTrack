using FluentValidation;

namespace RenoTrack.Application.Projects.Commands.ConvertAngebotToProject;

/// <summary>
/// Shape only (CLAUDE.md §5). BR-2's "the Angebot must be CustomerApproved" needs the loaded
/// aggregate, so it lives in the handler, never here — a validator never queries a repository.
/// </summary>
public sealed class ConvertAngebotToProjectCommandValidator : AbstractValidator<ConvertAngebotToProjectCommand>
{
    public ConvertAngebotToProjectCommandValidator()
    {
        RuleFor(c => c.AngebotId).GreaterThan(0);
        RuleFor(c => c.PerformedByAdminId).GreaterThan(0);
    }
}
