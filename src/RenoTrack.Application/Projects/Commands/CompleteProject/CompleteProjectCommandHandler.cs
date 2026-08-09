using FluentValidation;
using FluentValidation.Results;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Projects.Dtos;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Projects.Commands.CompleteProject;

/// <summary>
/// Marks a Project <c>Completed</c> (SRS FR-7.3, StateMachine.md §4.3, Sequence Diagram §10).
/// StateMachine.md §5 names this command as the enforcer of "a Project cannot silently become
/// <c>Completed</c> with unpaid Invoices", with FR-8.6's explicit override as the only way past it.
///
/// <para>
/// <b>Two guards, in two layers, and they must not be merged.</b> <c>Project.Complete()</c> owns
/// the aggregate's own state invariant (<c>Active</c> only — §4.2 draws no <c>OnHold →
/// Completed</c> edge) and is never re-checked here (CLAUDE.md §6). This handler owns only the
/// invoice precondition, which needs a different aggregate and so cannot live in the Domain
/// (CLAUDE.md §2). <b>The override bypasses the invoice precondition and nothing else</b> — no
/// value of <c>ForceOverride</c> can complete a Project that is not <c>Active</c>.
/// </para>
/// <para>
/// <b>The Project's own state guard runs first, and the ordering is load-bearing.</b>
/// <c>Complete()</c> is invoked before the invoice predicate is evaluated, so an <c>OnHold</c> or
/// already-<c>Completed</c> Project is refused for <i>its own state</i> in every combination of
/// invoice statuses and <c>ForceOverride</c> — never with an invoice-derived message, and never
/// with the "nothing to override" 400. An earlier draft evaluated the predicate first, which made
/// a settled-invoice <c>Completed</c> Project report 400 instead of 409; four tests now pin all
/// four combinations so it cannot drift back.
/// </para>
/// <para>
/// <b>The blocking predicate</b> (Phase 8 Slice 6, decisions K-1 and I-2) is stated on
/// <see cref="IInvoiceRepository.HasCompletionBlockingInvoicesForProjectAsync"/>: no Invoices at
/// all, or at least one <c>Draft</c>/<c>Sent</c>/<c>Overdue</c>. It resolves a contradiction
/// between StateMachine.md §3.4, §4.3 and Sequence Diagram §10 in favour of §4.3, reconciled in
/// StateMachine.md itself rather than silently chosen.
/// </para>
/// <para>
/// <b>An override that overrides nothing is a 400, not a quiet success</b> (decision I-3). The
/// caller asserted a blocking condition that does not exist, so the request is refused and
/// <b>no audit entry is written</b> — recording a "override: &lt;reason&gt;" row against a Project
/// that had nothing to override would put a false justification into the permanent record.
/// </para>
/// <para>
/// <b>No <c>IOwnershipValidator</c></b> — `PermissionMatrix.md` §5 marks "Mark Project Completed
/// (incl. override)" Admin <c>F</c>; an ownership check on an <c>F</c> action is a semantic error,
/// not merely redundant (CLAUDE.md §16).
/// </para>
/// <para>
/// <b>One <c>SaveChangesAsync</c>, no explicit transaction</b> (G-7) — a single status change on a
/// single aggregate, already atomic under EF Core's implicit per-save transaction. D48's amendment
/// exists for a genuine multi-save identity problem, not for symmetry.
/// </para>
/// <para>
/// <b>No notification.</b> FR-9.1 covers sending an Angebot or Invoice; FR-9.2's three Admin
/// triggers are a new website Lead, an Angebot submitted for review, and a Lead's decision.
/// Sequence Diagram §10 draws no mail participant at all. This handler therefore takes no
/// <see cref="IEmailSender"/>, and a test pins that by reflection so one cannot be added without a
/// visible signature change to review against FR-9.1/FR-9.2.
/// </para>
/// <para>
/// <b>Known race, stated rather than solved:</b> the predicate is read before the commit, so an
/// Invoice created or sent concurrently between the read and <c>SaveChangesAsync</c> would not be
/// seen. No document requires locking here, and inventing one would be policy rather than
/// implementation.
/// </para>
/// </summary>
public sealed class CompleteProjectCommandHandler(
    IValidator<CompleteProjectCommand> validator,
    IProjectRepository projectRepository,
    IInvoiceRepository invoiceRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<CompleteProjectCommand, ProjectDto>
{
    public async Task<ProjectDto> HandleAsync(CompleteProjectCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var project = await projectRepository.GetByIdAsync(command.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        // The Project's own state guard runs FIRST, and it runs by invoking the transition rather
        // than by inspecting `Status` here — a handler must never re-check an aggregate's state
        // field (CLAUDE.md §6), and a `CanComplete()` probe added just so this layer could look is
        // exactly what §2 forbids. So `Complete()` *is* step "verify the Project is Active": an
        // OnHold or already-Completed Project throws here, before any Invoice is consulted, and no
        // value of ForceOverride can reach past it.
        //
        // The aggregate is mutated in memory before the invoice precondition is evaluated below,
        // and that is safe rather than merely tolerable: every refusal path throws, so
        // `SaveChangesAsync` is never reached and the request-scoped DbContext is disposed with the
        // change discarded. The same shape `CompleteInspectionCommandHandler` has carried since
        // Phase 4 Slice 9 — safety from scope lifetime, not from a guard. Nothing may be inserted
        // between here and the refusals that commits or audits; `IAuditService` in particular
        // shares this DbContext, so an audit call placed here would flush this mutation.
        project.Complete();

        var blocked = await invoiceRepository.HasCompletionBlockingInvoicesForProjectAsync(
            command.ProjectId, cancellationToken);

        if (blocked && !command.ForceOverride)
        {
            throw new ConflictException(
                $"Project {project.Id} cannot be completed: it has no Invoices, or one or more " +
                "Invoices are still Draft, Sent or Overdue. Completing anyway requires an " +
                "explicit override with a reason (FR-8.6).");
        }

        if (!blocked && command.ForceOverride)
        {
            // FluentValidation's own exception type, so both of this endpoint's 400s share one
            // field-keyed body shape (CLAUDE.md §22) rather than differing by which layer refused.
            throw new ValidationException(
            [
                new ValidationFailure(
                    nameof(CompleteProjectCommand.ForceOverride),
                    $"Project {project.Id} has no blocking Invoices, so there is nothing to override.")
            ]);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // After the commit, always (D50). `details` carries the override reason and nothing else:
        // on the normal path there is no reason to carry, and inventing filler text would make the
        // audit trail's one meaningful free-text field unreadable.
        await auditService.LogAsync(
            entityType: nameof(Project),
            entityId: project.Id,
            action: AuditAction.ProjectCompleted,
            performedByUserId: command.CompletedByAdminId,
            details: command.ForceOverride ? command.Reason : null,
            cancellationToken);

        return project.ToDto();
    }
}
