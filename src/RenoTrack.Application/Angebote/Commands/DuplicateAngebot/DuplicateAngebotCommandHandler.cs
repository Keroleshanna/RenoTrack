using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Commands.DuplicateAngebot;

/// <summary>
/// Copies an entire Angebot into a new Draft on a target Lead (SRS FR-4.11).
/// </summary>
/// <remarks>
/// <para>
/// The result is indistinguishable from one built by hand: a fresh <c>AngebotNumber</c>, status
/// <c>Draft</c>, no reviewer, no timestamps carried over. Only the section/item tree is copied, and
/// it is copied through the aggregate's own <c>AddSection</c>/<c>AddItemToSection</c> methods — so
/// totals are recalculated by the same code path as any other edit, never assigned.
/// </para>
/// <para>
/// <b>The source's <c>InspectionId</c> is deliberately not copied.</b> It points at an Inspection
/// belonging to the <em>source</em> Lead, and carrying it over would attach the new Angebot to
/// another Lead's site visit. Nothing documents duplicating into a specific Inspection, so the copy
/// starts with none.
/// </para>
/// <para>
/// <b><c>CatalogItemId</c> <em>is</em> copied.</b> BR-8 defines it as "a traceability link only, not
/// a live reference" — every item carries its own copy of description, specification, unit and
/// price, so a duplicated line depends on nothing in the Catalog's current state. BR-12 guarantees
/// the row is never physically deleted, so the link cannot dangle, and BR-14 keeps a retired item a
/// valid reference. Dropping it would erase a true fact about where the line came from and make the
/// copy look hand-typed, with nothing gained.
/// </para>
/// <para>
/// Duplicating a single section is deliberately not built. FR-4.11 permits it, but the roadmap's
/// outcome does not depend on it and no wireframe shows it — it needs its own endpoint shape and
/// should arrive with a real caller (CLAUDE.md §4).
/// </para>
/// </remarks>
public sealed class DuplicateAngebotCommandHandler(
    IValidator<DuplicateAngebotCommand> validator,
    ILeadRepository leadRepository,
    IAngebotRepository angebotRepository,
    INumberGeneratorService numberGenerator,
    IOwnershipValidator ownershipValidator,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<DuplicateAngebotCommand, AngebotDto>
{
    public async Task<AngebotDto> HandleAsync(DuplicateAngebotCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var source = await angebotRepository.GetByIdAsync(command.SourceAngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), command.SourceAngebotId);

        // Two separate ownership rules, both required: the Inspector must own what they are copying
        // from, and the Lead they are copying to.
        ownershipValidator.EnsureAngebotOwnership(source, command.InspectorId);

        var targetLead = await leadRepository.GetByIdAsync(command.TargetLeadId, cancellationToken)
            ?? throw new NotFoundException(nameof(Lead), command.TargetLeadId);

        ownershipValidator.EnsureLeadOwnership(targetLead, command.InspectorId);

        // StateMachine.md §2.4, the same guard CreateAngebotCommand applies — duplicating must not
        // become a second route around "one non-terminal Angebot per Lead".
        if (await angebotRepository.HasActiveAngebotForLeadAsync(command.TargetLeadId, cancellationToken))
        {
            throw new ConflictException($"Lead {command.TargetLeadId} already has an active Angebot.");
        }

        var angebotNumber = await numberGenerator.NextAngebotNumberAsync(DateTime.UtcNow.Year, cancellationToken);

        var duplicate = Angebot.Create(
            command.TargetLeadId,
            inspectionId: null,
            angebotNumber,
            command.InspectorId);

        CopyTree(source, duplicate);

        targetLead.MarkAngebotInProgress(); // Domain self-guard (Status == InspectionDone)

        await angebotRepository.AddAsync(duplicate, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Against the Lead, not the Angebot: creating the draft is what drives
        // MarkAngebotInProgress, a Lead-level milestone — identical to CreateAngebotCommand (§10).
        await auditService.LogAsync(
            entityType: nameof(Lead),
            entityId: targetLead.Id,
            action: AuditAction.AngebotCreated,
            performedByUserId: command.InspectorId,
            details: null,
            cancellationToken);

        return duplicate.ToDto();
    }

    /// <summary>
    /// Rebuilds the tree through the aggregate's public API rather than cloning objects, so the copy
    /// is subject to every guard and recalculation a hand-built Angebot is.
    /// </summary>
    private static void CopyTree(Angebot source, Angebot duplicate)
    {
        foreach (var section in source.Sections.OrderBy(s => s.SortOrder).ThenBy(s => s.Id))
        {
            var copiedSection = duplicate.AddSection(section.Title, section.SortOrder);

            foreach (var item in section.Items)
            {
                duplicate.AddItemToSection(
                    copiedSection,
                    item.Description,
                    item.Quantity,
                    item.Unit,
                    item.UnitPrice,
                    item.VatRate,
                    item.Specification,
                    item.CatalogItemId);
            }
        }
    }
}
