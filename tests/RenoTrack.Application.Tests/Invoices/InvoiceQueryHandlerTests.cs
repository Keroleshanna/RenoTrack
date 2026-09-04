using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Invoices.Dtos;
using RenoTrack.Application.Invoices.Queries.GetInvoices;
using RenoTrack.Application.Invoices.Queries.GetReceivables;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Invoices;

/// <summary>
/// The two Invoice reads Phase 10 introduced.
/// </summary>
/// <remarks>
/// These cover the handler's own responsibilities — shape validation and passing filters through
/// untouched. The receivables arithmetic is SQL and is tested against a real database in
/// <c>RenoTrack.Infrastructure.Tests</c>, because that is where a wrong <c>SUM</c> would actually
/// surface (D40: the InMemory provider would prove nothing about it).
/// </remarks>
public sealed class InvoiceQueryHandlerTests
{
    private readonly FakeInvoiceQueries _queries = new();

    // ---- List ------------------------------------------------------------------------------------

    private GetInvoicesQueryHandler ListHandler() => new(new GetInvoicesQueryValidator(), _queries);

    [Fact]
    public async Task Passes_every_filter_through_to_the_query()
    {
        var dueBefore = new DateTime(2026, 8, 15);

        await ListHandler().HandleAsync(
            new GetInvoicesQuery(InvoiceStatus.Sent, ProjectId: 7, DueBefore: dueBefore, Page: 2, PageSize: 50),
            default);

        var call = Assert.Single(_queries.PagedCalls);
        Assert.Equal(InvoiceStatus.Sent, call.Status);
        Assert.Equal(7, call.ProjectId);
        Assert.Equal(dueBefore, call.DueBefore);
        Assert.Equal(2, call.Page);
        Assert.Equal(50, call.PageSize);
    }

    [Fact]
    public async Task Rejects_a_page_size_above_the_maximum()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => ListHandler().HandleAsync(
                new GetInvoicesQuery(null, null, null, PageSize: Pagination.MaxPageSize + 1),
                default));
    }

    [Fact]
    public async Task Rejects_a_status_that_is_not_a_real_enum_value()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => ListHandler().HandleAsync(
                new GetInvoicesQuery((InvoiceStatus)99, null, null),
                default));
    }

    [Fact]
    public async Task Rejects_a_non_positive_project_filter()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => ListHandler().HandleAsync(new GetInvoicesQuery(null, ProjectId: 0, null), default));
    }

    // ---- Receivables -----------------------------------------------------------------------------

    private GetReceivablesQueryHandler ReceivablesHandler() =>
        new(new GetReceivablesQueryValidator(), _queries);

    /// <summary>
    /// The caller's date decides what counts as overdue, so a client in another timezone is never
    /// told a different number than the one it computed locally.
    /// </summary>
    [Fact]
    public async Task Uses_the_callers_as_of_date_rather_than_the_server_clock()
    {
        var asOf = new DateTime(2026, 3, 1);

        await ReceivablesHandler().HandleAsync(new GetReceivablesQuery(asOf), default);

        Assert.Equal(asOf, Assert.Single(_queries.ReceivablesCalls));
    }

    [Fact]
    public async Task Rejects_a_missing_as_of_date()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => ReceivablesHandler().HandleAsync(new GetReceivablesQuery(default), default));
    }

    [Fact]
    public async Task Returns_the_summary_the_query_produced()
    {
        _queries.Receivables = new ReceivablesSummaryDto(
            InvoicedGross: 100_000m,
            PaidGross: 70_000m,
            OpenGross: 30_000m,
            OverdueGross: 12_000m,
            VoidedGross: 5_000m,
            InvoiceCount: 12,
            OpenCount: 4,
            OverdueCount: 2);

        var result = await ReceivablesHandler().HandleAsync(
            new GetReceivablesQuery(new DateTime(2026, 8, 15)),
            default);

        Assert.Equal(100_000m, result.InvoicedGross);
        Assert.Equal(12_000m, result.OverdueGross);
        Assert.Equal(2, result.OverdueCount);
    }
}
