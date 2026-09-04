using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Projects.Dtos;

/// <summary>
/// One row of the Project list — the Projekte workspace and the Cockpit's "projects under way".
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately lighter than <see cref="ProjectDetailDto"/>.</b> That type carries the full
/// invoice list and the derived balance, which it computes per Project; doing the same for every row
/// of a page would mean one balance calculation per Project on every list read.
/// </para>
/// <para>
/// <b>No <c>AlreadyInvoiced</c> / <c>Remaining</c> here, and that is a deliberate omission rather
/// than a gap.</b> A screen that needs money per Project reads
/// <c>GET /api/v1/invoices?projectId=</c> or the Project's own detail endpoint, both of which
/// already exist and are already the authority for it. Duplicating the calculation into a list
/// projection would create a second place for it to drift.
/// </para>
/// <para>
/// <b>There is no project title.</b> `ERD.md` gives `Project` no title column, although Wireframe E1
/// renders *"Project: M. Klein — Bathroom Renovation"*. That gap is real and pre-dates Phase 10; a
/// title is not invented here. The customer's name plus the originating Angebot number is what the
/// schema can actually identify a Project by.
/// </para>
/// </remarks>
public sealed record ProjectListItemDto(
    int Id,
    ProjectStatus Status,
    decimal AgreedTotal,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    int CustomerId,
    string CustomerName,
    int AngebotId,
    string AngebotNumber);
