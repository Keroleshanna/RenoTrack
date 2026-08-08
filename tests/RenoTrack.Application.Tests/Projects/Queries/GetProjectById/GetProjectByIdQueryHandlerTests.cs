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
        LeadId: 3, InspectionId: 9, AngebotId: 42, AngebotNumber: "ANG-2026-00042");

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

}
