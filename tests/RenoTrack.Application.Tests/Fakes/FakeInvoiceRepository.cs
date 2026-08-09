using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// Mirrors a real repository by <b>not</b> assigning an id on <c>AddAsync</c> — EF Core assigns
/// identity at <c>SaveChanges</c>, so a handler that used an unsaved id would fail here exactly as
/// it would against SQL Server. The same shape <c>FakeCustomerRepository</c> established in Phase 7,
/// for the same reason.
/// </summary>
public sealed class FakeInvoiceRepository : IInvoiceRepository
{
    public List<Invoice> AddedInvoices { get; } = [];

    public Task AddAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        AddedInvoices.Add(invoice);
        return Task.CompletedTask;
    }

    private readonly Dictionary<int, Invoice> _byId = [];

    public Task<Invoice?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.GetValueOrDefault(id));

    /// <summary>Simulates database-assigned identity — reflection, test infrastructure only (CLAUDE.md §14).</summary>
    public Invoice Seed(Invoice invoice, int id)
    {
        typeof(Invoice).GetProperty(nameof(Invoice.Id))!.SetValue(invoice, id);
        _byId[id] = invoice;
        return invoice;
    }

    /// <summary>
    /// The blocking predicate, evaluated over the seeded Invoices exactly as the real repository
    /// evaluates it in SQL: no Invoices at all, or at least one <c>Draft</c>/<c>Sent</c>/
    /// <c>Overdue</c>. Written out rather than delegated so the rule is visible in the fake too —
    /// if the two ever disagree, a test should be what notices.
    /// </summary>
    public Task<bool> HasCompletionBlockingInvoicesForProjectAsync(int projectId, CancellationToken cancellationToken)
    {
        HasCompletionBlockingInvoicesCallCount++;

        var invoices = _byId.Values.Where(invoice => invoice.ProjectId == projectId).ToList();

        return Task.FromResult(
            invoices.Count == 0
            || invoices.Any(invoice => invoice.Status
                is InvoiceStatus.Draft or InvoiceStatus.Sent or InvoiceStatus.Overdue));
    }

    public int HasCompletionBlockingInvoicesCallCount { get; private set; }
}
