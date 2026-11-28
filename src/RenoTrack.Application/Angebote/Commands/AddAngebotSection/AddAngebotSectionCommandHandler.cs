using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Commands.AddAngebotSection;

/// <summary>
/// Sequence Diagram §4 "Add a Section". Editability (Draft/ChangesRequested only), the
/// implicit ChangesRequested→Draft transition, and totals recalculation all live entirely
/// inside Angebot.AddSection — not duplicated here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Owning Inspector or any Admin</b> (<c>PermissionMatrix.md</c> §3, D115). The Inspector is
/// ownership-checked; an Admin is <c>F</c> and is not (CLAUDE.md §16), which is what a <c>null</c>
/// <c>RequestingInspectorId</c> expresses (D88).
/// </para>
/// <para>
/// <b>Audited only when an Admin edits</b> (<c>AngebotEditedByAdmin</c>). An Inspector building
/// their own draft is in-progress work and stays unaudited (CLAUDE.md §10), even on the occasion it
/// triggers the internal ChangesRequested→Draft adjustment.
/// </para>
/// </remarks>
public sealed class AddAngebotSectionCommandHandler(
    IValidator<AddAngebotSectionCommand> validator,
    IAngebotRepository angebotRepository,
    IOwnershipValidator ownershipValidator,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<AddAngebotSectionCommand, SectionDto>
{
    public async Task<SectionDto> HandleAsync(AddAngebotSectionCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var angebot = await angebotRepository.GetByIdAsync(command.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), command.AngebotId);

        if (command.RequestingInspectorId is { } inspectorId)
        {
            ownershipValidator.EnsureAngebotOwnership(angebot, inspectorId);
        }

        AngebotFreshness.EnsureExpectedVersion(angebot, command.ExpectedVersion);

        var section = angebot.AddSection(command.Title, command.SortOrder);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (command.RequestingInspectorId is null)
        {
            await auditService.LogAsync(
                entityType: nameof(Angebot),
                entityId: angebot.Id,
                action: AuditAction.AngebotEditedByAdmin,
                performedByUserId: command.PerformedByUserId,
                details: AngebotAdminEditAudit.Details(AngebotEditOperation.SectionAdded, section.Id),
                cancellationToken);
        }

        return section.ToDto();
    }
}
