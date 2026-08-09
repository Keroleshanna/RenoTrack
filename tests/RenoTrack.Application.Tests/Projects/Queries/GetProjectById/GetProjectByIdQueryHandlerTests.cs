using FluentValidation;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Projects;
using RenoTrack.Application.Projects.Dtos;
using RenoTrack.Application.Projects.Queries.GetProjectById;
using RenoTrack.Domain.Enums;
using RenoTrack.Application.Tests.Fakes;

namespace RenoTrack.Application.Tests.Projects.Queries.GetProjectById;

public class GetProjectByIdQueryHandlerTests
{
    private readonly FakeProjectQueries _projectQueries = new();
    private readonly GetProjectByIdQueryHandler _handler;

    public GetProjectByIdQueryHandlerTests()
    {
        _handler = new GetProjectByIdQueryHandler(new GetProjectByIdQueryValidator(), _projectQueries);
    }

    private static ProjectDetailDto Detail(int id = 1) => new(
        id, ProjectStatus.Active, 25_673.36m, DateTime.UtcNow, null,
        CustomerId: 7, CustomerName: "M. Klein",
        LeadId: 3, InspectionId: 9, AngebotId: 42, AngebotNumber: "ANG-2026-00042",
        AlreadyInvoiced: 8_000m, Remaining: 17_673.36m,
        Invoices:
        [
            new ProjectInvoiceDto(11, "RE-2026-00017", 8_000m, InvoiceStatus.Sent, new DateTime(2026, 8, 15)),
        ]);

    [Fact]
    public async Task AnExistingProjectIsReturned()
    {
        _projectQueries.Seed(Detail(id: 5));

        var result = await _handler.HandleAsync(new GetProjectByIdQuery(5), CancellationToken.None);

        Assert.Equal(5, result.Id);
        Assert.Equal("M. Klein", result.CustomerName);
        Assert.Equal("ANG-2026-00042", result.AngebotNumber);
        Assert.Equal(25_673.36m, result.AgreedTotal);
    }

    [Fact]
    public async Task AMissingProjectThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new GetProjectByIdQuery(404), CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AMalformedIdFailsValidation(int id)
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new GetProjectByIdQuery(id), CancellationToken.None));
    }

    /// <summary>
    /// The read is unscoped: `PermissionMatrix.md` §5 marks it Admin "F" / Inspector "R", and "R" is
    /// read-only rather than scoped. This pins the query's shape — a caller cannot pass an Inspector
    /// id even if someone later wanted a per-Inspector restriction, so reintroducing one would be a
    /// visible signature change reviewed against §5, not a quiet predicate added to a WHERE clause.
    /// </summary>
    [Fact]
    public void TheQueryCarriesNoRequestingInspectorId()
    {
        var parameters = typeof(GetProjectByIdQuery)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.Name!)
            .ToArray();

        Assert.Equal([nameof(GetProjectByIdQuery.Id)], parameters);
    }

    /// <summary>
    /// FR-7.4's invoice portion, added in Phase 8 Slice 6, passes through untouched — the handler
    /// neither recomputes the figures nor filters the list. Both belong to the query implementation,
    /// which is tested against real SQL in <c>ProjectQueriesTests</c>.
    /// </summary>
    [Fact]
    public async Task TheInvoicePortionIsReturnedUnchanged()
    {
        _projectQueries.Seed(Detail(id: 5));

        var result = await _handler.HandleAsync(new GetProjectByIdQuery(5), CancellationToken.None);

        Assert.Equal(8_000m, result.AlreadyInvoiced);
        Assert.Equal(17_673.36m, result.Remaining);
        var invoice = Assert.Single(result.Invoices);
        Assert.Equal("RE-2026-00017", invoice.InvoiceNumber);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
    }

    /// <summary>
    /// The invoice rows carry E1's four columns plus the id the "Mark Paid" button needs, and
    /// nothing more — no net/VAT split, no issue date, no void reason, no payments. Pinned by
    /// property name so an added field is a deliberate, reviewed change rather than a quiet one.
    /// </summary>
    [Fact]
    public void AnInvoiceRowExposesOnlyWhatWireframeE1Renders()
    {
        var properties = typeof(ProjectInvoiceDto)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            ["DueDate", "GrossAmount", "Id", "InvoiceNumber", "Status"],
            properties);
    }
}
