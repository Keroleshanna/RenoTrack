using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Projects.Dtos;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Projects.Commands.PutProjectOnHold;

/// <summary>
/// Pauses an active Project (StateMachine.md §4.3). The canonical CLAUDE.md §6 shape at its
/// smallest: validate, load, invoke one Domain method, persist, audit, map.
/// </summary>
/// <remarks>
/// <para>
/// <b>No status check here.</b> <c>Project.PutOnHold()</c> owns the <c>Active</c>-only invariant
/// and the handler calls it and lets it throw (CLAUDE.md §6), surfacing as 409 through the
/// <c>InvalidOperationException</c> arm of the ProblemDetails switch. Re-checking would duplicate
/// a Domain guard in the layer above it.
/// </para>
/// <para>
/// <b>No ownership check</b> — §5 marks this row Admin <c>F</c> (CLAUDE.md §16). <b>No
/// notification</b> — FR-9.1/FR-9.2 enumerate the documented triggers and pausing work is not
/// among them, so adding one would be speculative (CLAUDE.md §11). <b>Nothing cascades to the
/// Invoices</b>: StateMachine.md §5 lets an Invoice exist against an <c>Active</c> <i>or</i>
/// <c>OnHold</c> Project, so pausing must not disturb billing that has already gone out.
/// </para>
/// </remarks>
public sealed class PutProjectOnHoldCommandHandler(
    IValidator<PutProjectOnHoldCommand> validator,
    IProjectRepository projectRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<PutProjectOnHoldCommand, ProjectDto>
{
    public async Task<ProjectDto> HandleAsync(
        PutProjectOnHoldCommand command,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var project = await projectRepository.GetByIdAsync(command.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        project.PutOnHold();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Project),
            entityId: project.Id,
            action: AuditAction.ProjectPutOnHold,
            performedByUserId: command.PutOnHoldByAdminId,
            details: null,
            cancellationToken);

        return project.ToDto();
    }
}
