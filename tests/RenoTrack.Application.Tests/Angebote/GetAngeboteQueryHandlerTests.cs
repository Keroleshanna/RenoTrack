using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Angebote.Queries.GetAngebote;
using RenoTrack.Application.Common;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Angebote;

/// <summary>
/// The cross-Lead Angebot list.
/// </summary>
/// <remarks>
/// The behaviour worth testing here is **what reaches the query**, not what comes back: this is a
/// list read, so its scoping is a <c>WHERE</c> clause rather than an <c>IOwnershipValidator</c> call,
/// and a regression would silently widen one role's visibility rather than throw. That is exactly
/// the class of bug a test has to catch, because nothing else would.
/// </remarks>
public sealed class GetAngeboteQueryHandlerTests
{
    private readonly FakeAngebotQueries _queries = new();
    private readonly GetAngeboteQueryHandler _handler;

    public GetAngeboteQueryHandlerTests()
    {
        _handler = new GetAngeboteQueryHandler(new GetAngeboteQueryValidator(), _queries);
    }

    [Fact]
    public async Task Passes_an_inspectors_own_id_through_as_the_scope()
    {
        await _handler.HandleAsync(new GetAngeboteQuery(null, RequestingInspectorId: 42), default);

        var call = Assert.Single(_queries.PagedCalls);
        Assert.Equal(42, call.RequestingInspectorId);
    }

    /// <summary>
    /// Null is "unrestricted", and PermissionMatrix.md §4 marks the Admin "F". The API layer decides
    /// which of the two a caller is; the handler must not reinterpret it.
    /// </summary>
    [Fact]
    public async Task Passes_null_through_unchanged_for_an_admin()
    {
        await _handler.HandleAsync(new GetAngeboteQuery(null, RequestingInspectorId: null), default);

        var call = Assert.Single(_queries.PagedCalls);
        Assert.Null(call.RequestingInspectorId);
    }

    [Fact]
    public async Task Passes_the_status_filter_and_paging_through()
    {
        await _handler.HandleAsync(
            new GetAngeboteQuery(AngebotStatus.InReview, null, Page: 3, PageSize: 10),
            default);

        var call = Assert.Single(_queries.PagedCalls);
        Assert.Equal(AngebotStatus.InReview, call.Status);
        Assert.Equal(3, call.Page);
        Assert.Equal(10, call.PageSize);
    }

    [Fact]
    public async Task Returns_the_page_the_query_produced()
    {
        _queries.PagedResult = new PagedResult<AngebotListItemDto>(
            [new AngebotListItemDto(1, "ANG-2026-00001", 7, "M. Klein", AngebotStatus.InReview, 100m, 119m, 5, DateTime.UtcNow, null, null)],
            Page: 1,
            PageSize: 25,
            TotalCount: 1);

        var result = await _handler.HandleAsync(new GetAngeboteQuery(null, null), default);

        var row = Assert.Single(result.Items);
        Assert.Equal("ANG-2026-00001", row.AngebotNumber);
        Assert.Equal("M. Klein", row.LeadName);
        Assert.Equal(1, result.TotalCount);
    }

    // ---- Shape validation (CLAUDE.md §5) ---------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Rejects_a_page_below_the_first(int page)
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new GetAngeboteQuery(null, null, Page: page), default));
    }

    /// <summary>
    /// The bound exists so a caller cannot turn a paged endpoint back into an unbounded one — the
    /// reason Architecture.md §5.1 mandates pagination at all.
    /// </summary>
    [Fact]
    public async Task Rejects_a_page_size_above_the_maximum()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(
                new GetAngeboteQuery(null, null, PageSize: Pagination.MaxPageSize + 1),
                default));
    }

    [Fact]
    public async Task Rejects_a_status_that_is_not_a_real_enum_value()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new GetAngeboteQuery((AngebotStatus)999, null), default));
    }

    /// <summary>A filter matching nothing is an empty page, never an error.</summary>
    [Fact]
    public async Task Accepts_a_status_that_matches_nothing()
    {
        var result = await _handler.HandleAsync(
            new GetAngeboteQuery(AngebotStatus.CustomerRejected, null),
            default);

        Assert.Empty(result.Items);
    }
}
