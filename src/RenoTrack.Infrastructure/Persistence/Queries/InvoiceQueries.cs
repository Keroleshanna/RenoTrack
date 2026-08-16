using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common;
using RenoTrack.Application.Invoices;
using RenoTrack.Application.Invoices.Dtos;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Infrastructure.Persistence.Queries;

/// <inheritdoc />
/// <remarks>
/// <para>
/// The customer's name is reached by joining <c>Invoices → Projects → Customers</c> inside the
/// projection. The aggregates still relate by id only — nothing here adds a navigation property —
/// which is exactly the latitude a read-side interface exists to provide (D36).
/// </para>
/// <para>
/// <c>AsNoTracking</c> throughout: nothing on this side is ever mutated.
/// </para>
/// </remarks>
public sealed class InvoiceQueries(RenoTrackDbContext dbContext) : IInvoiceQueries
{
    public async Task<PagedResult<InvoiceListItemDto>> GetPagedAsync(
        InvoiceStatus? status,
        int? projectId,
        DateTime? dueBefore,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Invoices.AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        if (projectId.HasValue)
        {
            query = query.Where(i => i.ProjectId == projectId.Value);
        }

        if (dueBefore.HasValue)
        {
            query = query.Where(i => i.DueDate < dueBefore.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Oldest due date first: an invoice list is a chase list, and the most overdue money is the
        // most urgent. Id breaks ties, since DueDate is emphatically not unique.
        var items = await query
            .OrderBy(i => i.DueDate)
            .ThenBy(i => i.Id)
            .Skip((page - Pagination.FirstPage) * pageSize)
            .Take(pageSize)
            .Join(
                dbContext.Projects.AsNoTracking(),
                invoice => invoice.ProjectId,
                project => project.Id,
                (invoice, project) => new { invoice, project })
            .Join(
                dbContext.Customers.AsNoTracking(),
                pair => pair.project.CustomerId,
                customer => customer.Id,
                (pair, customer) => new InvoiceListItemDto(
                    pair.invoice.Id,
                    pair.invoice.InvoiceNumber,
                    pair.invoice.ProjectId,
                    customer.Id,
                    customer.Name,
                    pair.invoice.Status,
                    pair.invoice.IssueDate,
                    pair.invoice.DueDate,
                    pair.invoice.NetAmount.Amount,
                    pair.invoice.VatAmount.Amount,
                    pair.invoice.GrossAmount.Amount,

                    // Derived from the Payment child rather than a column — see InvoiceListItemDto.
                    // Max over a collection that MarkPaid guarantees holds at most one row.
                    pair.invoice.Payments
                        .Select(p => (DateTime?)p.PaidAt)
                        .Max(),
                    pair.invoice.VoidReason))
            .ToListAsync(cancellationToken);

        return new PagedResult<InvoiceListItemDto>(items, page, pageSize, totalCount);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Aggregated in SQL via <c>EF.Property</c>, which is what lets a <c>SUM</c> see through the
    /// <c>Money</c> value converter. The implementation note records why <c>.Amount</c> cannot.
    /// </para>
    /// <para>
    /// <b><c>Void</c> is excluded from every figure except its own.</b> BR-9 retains a voided invoice
    /// for its number, not as money owed; counting it as outstanding would invent a receivable the
    /// company has explicitly cancelled. It is reported separately so the totals still reconcile
    /// against the raw book.
    /// </para>
    /// </remarks>
    public async Task<ReceivablesSummaryDto> GetReceivablesAsync(
        DateTime asOf,
        CancellationToken cancellationToken)
    {
        // ---- EF.Property, not .Amount ---------------------------------------------------------
        //
        // **EF Core cannot aggregate over a value-converted property.** `GrossAmount` is a `Money`
        // behind `MoneyConverter`; reading `.Amount` off it translates in a plain projection (the
        // list read above relies on exactly that) but **not inside SUM**, where EF raises
        // `InvalidOperationException` rather than silently evaluating on the client. Three SQL-side
        // formulations were tried here and all three threw against real SQL Server before this one
        // — every failure at runtime, and every one of them would have passed under the InMemory
        // provider, which is D40's argument demonstrated.
        //
        // `EF.Property<decimal>` names the provider-side column directly, so the SUM runs in SQL.
        // This is the same solution `ProjectQueries.GetInvoiceBalanceAsync` already documents for
        // the same constraint — one convention, not two.
        var live = dbContext.Invoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Void);
        var overdue = live.Where(i => i.Status == InvoiceStatus.Sent && i.DueDate < asOf);

        // SQL's SUM over no rows returns NULL, so each is coalesced — a company that has never
        // invoiced must report 0, not null.
        static async Task<decimal> SumGrossAsync(IQueryable<Domain.Entities.Invoice> source, CancellationToken ct) =>
            await source.SumAsync(i => (decimal?)EF.Property<decimal>(i, nameof(Domain.Entities.Invoice.GrossAmount)), ct) ?? 0m;

        // A Draft counts as money the company still intends to collect, which is why D67 lets one
        // block Project completion. Only Void is excluded.
        var invoicedGross = await SumGrossAsync(live, cancellationToken);
        var paidGross = await SumGrossAsync(live.Where(i => i.Status == InvoiceStatus.Paid), cancellationToken);
        var overdueGross = await SumGrossAsync(overdue, cancellationToken);

        // Reported separately rather than hidden, so the totals still reconcile against the raw
        // invoice book (BR-9 retains a voided invoice for its number, not as money owed).
        var voidedGross = await SumGrossAsync(
            dbContext.Invoices.AsNoTracking().Where(i => i.Status == InvoiceStatus.Void),
            cancellationToken);

        return new ReceivablesSummaryDto(
            invoicedGross,
            paidGross,

            // Open is the remainder by construction, so paid + open can never fail to equal
            // invoiced — a subtraction cannot drift from its own operands the way two independently
            // computed sums could.
            invoicedGross - paidGross,
            overdueGross,
            voidedGross,
            await live.CountAsync(cancellationToken),
            await live.CountAsync(i => i.Status != InvoiceStatus.Paid, cancellationToken),
            await overdue.CountAsync(cancellationToken));
    }
}
