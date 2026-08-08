using Microsoft.EntityFrameworkCore;
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
    public async Task<ProjectDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await (from project in dbContext.Projects.AsNoTracking()
               join customer in dbContext.Customers.AsNoTracking() on project.CustomerId equals customer.Id
               join angebot in dbContext.Angebote.AsNoTracking() on project.AngebotId equals angebot.Id
               where project.Id == id
               select new ProjectDetailDto(
                   project.Id,
                   project.Status,
                   project.AgreedTotal.Amount,
                   project.CreatedAt,
                   project.CompletedAt,
                   customer.Id,
                   customer.Name,
                   angebot.LeadId,
                   angebot.InspectionId,
                   angebot.Id,
                   angebot.AngebotNumber))
            .SingleOrDefaultAsync(cancellationToken);

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
}
