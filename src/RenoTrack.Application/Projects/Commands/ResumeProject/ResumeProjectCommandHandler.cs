using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Projects.Dtos;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Projects.Commands.ResumeProject;

/// <summary>
/// Resumes a paused Project (StateMachine.md §4.3). The exact mirror of
/// <c>PutProjectOnHoldCommandHandler</c>, and kept as its own command rather than a single
/// "set status" endpoint taking the target state — this codebase names every transition
/// (CLAUDE.md §2, §10), and a status-setting endpoint would be the free-standing status edit
/// BR-7 exists to prevent.
/// </summary>
/// <remarks>
/// <c>Project.Resume()</c> owns the <c>OnHold</c>-only invariant; the handler calls it and lets it
/// throw (409). No ownership check (§5 is Admin <c>F</c>), no notification (none documented).
/// </remarks>
public sealed class ResumeProjectCommandHandler(
    IValidator<ResumeProjectCommand> validator,
    IProjectRepository projectRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<ResumeProjectCommand, ProjectDto>
{
    public async Task<ProjectDto> HandleAsync(
        ResumeProjectCommand command,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var project = await projectRepository.GetByIdAsync(command.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        project.Resume();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Project),
            entityId: project.Id,
            action: AuditAction.ProjectResumed,
            performedByUserId: command.ResumedByAdminId,
            details: null,
            cancellationToken);

        return project.ToDto();
    }
}
