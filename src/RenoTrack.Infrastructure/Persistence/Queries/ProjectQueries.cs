using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common;
using RenoTrack.Application.Projects;
using RenoTrack.Application.Projects.Dtos;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Infrastructure.Persistence.Queries;

/// <summary>
/// The Project detail read, projected in SQL across three tables.
///
/// <para>
/// <b>Joined explicitly rather than navigated.</b> `Project` holds no navigation property to
/// `Customer` or `Angebot` by design (CLAUDE.md §2), so there is nothing to <c>Include</c> — the
/// joins are written out. That is the read side paying a small, visible cost for a write-side
/// guarantee, not a workaround.
/// </para>
/// <para>
/// <b><c>LeadId</c> comes from the Angebot</b>, the originating document E1's "Originating:" line
/// names. `Customer.LeadId` holds the same value by construction — the conversion handler resolves
/// the Customer by the Angebot's own Lead — so the choice is about which one *means* "the Lead this
/// work came from", not about which happens to be populated.
/// </para>
/// </summary>
public sealed class ProjectQueries(RenoTrackDbContext dbContext) : IProjectQueries
{
    /// <summary>
    /// <b>Two statements, and the second is the invoice list FR-7.4 asks for.</b> They are not
    /// joined into one: a join would repeat every Project/Customer/Angebot column once per Invoice
    /// row, and a Project with no Invoices would still have to be distinguished from one with a
    /// null-filled outer join. Two indexed reads are cheaper to run and far easier to read.
    ///
    /// <para>
    /// <b><c>AlreadyInvoiced</c> is summed from the rows already fetched, not by a second SQL
    /// <c>SUM</c>.</b> The list is needed anyway and carries the same <c>GrossAmount</c>, so a
    /// separate aggregate query would be a third round trip computing what is already in hand —
    /// and it would hit the constraint <see cref="GetInvoiceBalanceAsync"/> documents (a
    /// value-converted <c>Money</c> does not translate inside an aggregate, only in a plain
    /// projection like this one). The exclusion rule is identical to that method's, and a test
    /// asserts the two endpoints report the same figures so the duplication cannot drift.
    /// </para>
    /// <para>
    /// <b>Ordered <c>IssueDate</c> then <c>Id</c>.</b> No document specifies an order, and an
    /// unordered list read can present rows differently between two identical requests;
    /// <c>IssueDate</c> is not unique (several Invoices are routinely raised the same day), so the
    /// primary key is the tiebreaker.
    /// </para>
    /// </summary>
    public async Task<ProjectDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var header = await (from project in dbContext.Projects.AsNoTracking()
                            join customer in dbContext.Customers.AsNoTracking() on project.CustomerId equals customer.Id
                            join angebot in dbContext.Angebote.AsNoTracking() on project.AngebotId equals angebot.Id
                            where project.Id == id
                            select new
                            {
                                project.Id,
                                project.Status,
                                AgreedTotal = project.AgreedTotal.Amount,
                                project.CreatedAt,
                                project.CompletedAt,
                                CustomerId = customer.Id,
                                CustomerName = customer.Name,
                                angebot.LeadId,
                                angebot.InspectionId,
                                AngebotId = angebot.Id,
                                angebot.AngebotNumber,
                            })
            .SingleOrDefaultAsync(cancellationToken);

        if (header is null)
            return null;

        var invoices = await dbContext.Invoices.AsNoTracking()
            .Where(invoice => invoice.ProjectId == id)
            .OrderBy(invoice => invoice.IssueDate)
            .ThenBy(invoice => invoice.Id)
            .Select(invoice => new ProjectInvoiceDto(
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.GrossAmount.Amount,
                invoice.Status,
                invoice.DueDate))
            .ToListAsync(cancellationToken);

        // StateMachine.md §3.3 excludes Void from remaining-balance math and excludes nothing else,
        // so Draft counts exactly as Paid does. Void rows stay in the list above regardless (BR-9).
        var alreadyInvoiced = invoices
            .Where(invoice => invoice.Status != InvoiceStatus.Void)
            .Sum(invoice => invoice.GrossAmount);

        return new ProjectDetailDto(
            header.Id,
            header.Status,
            header.AgreedTotal,
            header.CreatedAt,
            header.CompletedAt,
            header.CustomerId,
            header.CustomerName,
            header.LeadId,
            header.InspectionId,
            header.AngebotId,
            header.AngebotNumber,
            alreadyInvoiced,
            // Never clamped — a negative remainder is BR-3's warning, not an error state.
            header.AgreedTotal - alreadyInvoiced,
            invoices);
    }

    /// <summary>
    /// BR-3's running total, summed in SQL rather than by loading invoices into memory.
    ///
    /// <para>
    /// <b>Only <c>Void</c> is excluded</b> — StateMachine.md §3.3's "excluded from 'remaining
    /// balance' math going forward". Every other status counts, <c>Draft</c> included, because no
    /// document excludes any other.
    /// </para>
    /// <para>
    /// <b><c>Remaining</c> is not clamped.</b> BR-3 warns rather than blocks, so an over-invoiced
    /// Project reports a negative remainder — that value is the warning, and flooring it at zero
    /// would delete the only signal BR-3 asks the system to produce.
    /// </para>
    /// <para>
    /// The sum is written as a <c>decimal?</c> coalesced to zero: SQL's <c>SUM</c> over no rows
    /// returns <c>NULL</c>, so a Project with no invoices must yield 0, not null.
    /// </para>
    /// <para>
    /// <b>Two round trips, and <c>EF.Property</c> rather than <c>.Amount</c> — both forced by EF
    /// Core, and both found by these tests failing rather than by inspection.</b> Reading
    /// <c>.Amount</c> off a value-converted <see cref="RenoTrack.Domain.ValueObjects.Money"/>
    /// property translates in a plain projection (the detail read above relies on exactly that) but
    /// **not inside an aggregate**, and not inside a correlated subquery — EF raises
    /// <c>InvalidOperationException</c> rather than silently evaluating on the client, which is the
    /// good outcome. <c>EF.Property&lt;decimal&gt;</c> names the provider-side column directly, so
    /// the <c>SUM</c> runs in SQL. Both statements are ordinary indexed reads — the second is backed
    /// by <c>IX_Invoices_ProjectId</c> — so the cost is one extra round trip, not a scan.
    /// </para>
    /// </summary>
    public async Task<ProjectInvoiceBalanceDto?> GetInvoiceBalanceAsync(
        int projectId,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, AgreedTotal = p.AgreedTotal.Amount })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
            return null;

        var alreadyInvoiced = await dbContext.Invoices.AsNoTracking()
            .Where(invoice => invoice.ProjectId == projectId && invoice.Status != InvoiceStatus.Void)
            .SumAsync(invoice => (decimal?)EF.Property<decimal>(invoice, nameof(Invoice.GrossAmount)), cancellationToken)
            ?? 0m;

        return new ProjectInvoiceBalanceDto(
            project.Id,
            project.AgreedTotal,
            alreadyInvoiced,
            project.AgreedTotal - alreadyInvoiced);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Joined out explicitly to <c>Customers</c> and <c>Angebote</c>, for the same reason the detail
    /// read does: <c>Project</c> holds no navigation property to either by design (CLAUDE.md §2), so
    /// there is nothing to <c>Include</c>.
    ///
    /// <b>No per-row balance calculation.</b> Money per Project comes from the Invoice list or the
    /// Project's own detail read, both of which already own that arithmetic — see
    /// <see cref="ProjectListItemDto"/>.
    /// </remarks>
    public async Task<PagedResult<ProjectListItemDto>> GetPagedAsync(
        ProjectStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Projects.AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Ordered on the entity before projecting — the same translation constraint the Inspection
        // schedule ran into. Newest first; Id breaks ties, since CreatedAt is not unique.
        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Skip((page - Pagination.FirstPage) * pageSize)
            .Take(pageSize)
            .Join(
                dbContext.Customers.AsNoTracking(),
                project => project.CustomerId,
                customer => customer.Id,
                (project, customer) => new { project, customer })
            .Join(
                dbContext.Angebote.AsNoTracking(),
                pair => pair.project.AngebotId,
                angebot => angebot.Id,
                (pair, angebot) => new ProjectListItemDto(
                    pair.project.Id,
                    pair.project.Status,
                    pair.project.AgreedTotal.Amount,
                    pair.project.CreatedAt,
                    pair.project.CompletedAt,
                    pair.customer.Id,
                    pair.customer.Name,
                    angebot.Id,
                    angebot.AngebotNumber))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProjectListItemDto>(items, page, pageSize, totalCount);
    }
}
