using FluentValidation;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Projects.Dtos;
using RenoTrack.Application.Projects.Queries.GetProjectInvoiceBalance;
using RenoTrack.Application.Tests.Fakes;

namespace RenoTrack.Application.Tests.Projects.Queries.GetProjectInvoiceBalance;

public class GetProjectInvoiceBalanceQueryHandlerTests
{
    private readonly FakeProjectQueries _projectQueries = new();
    private readonly GetProjectInvoiceBalanceQueryHandler _handler;

    public GetProjectInvoiceBalanceQueryHandlerTests()
    {
        _handler = new GetProjectInvoiceBalanceQueryHandler(
            new GetProjectInvoiceBalanceQueryValidator(),
            _projectQueries);
    }

    [Fact]
    public async Task AKnownProjectReturnsItsBalance()
    {
        _projectQueries.Seed(new ProjectInvoiceBalanceDto(7, 25_673.36m, 8_000.00m, 17_673.36m));

        var result = await _handler.HandleAsync(new GetProjectInvoiceBalanceQuery(7), CancellationToken.None);

        Assert.Equal(25_673.36m, result.AgreedTotal);
        Assert.Equal(8_000.00m, result.AlreadyInvoiced);
        Assert.Equal(17_673.36m, result.Remaining);
    }

    [Fact]
    public async Task AnUnknownProjectIsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new GetProjectInvoiceBalanceQuery(999), CancellationToken.None));
    }

    [Fact]
    public async Task ANonPositiveIdFailsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new GetProjectInvoiceBalanceQuery(0), CancellationToken.None));
    }

    /// <summary>
    /// <b>BR-3's warning signal must survive the handler untouched.</b> An over-invoiced Project
    /// reports a negative remainder, and that negative *is* the warning — a handler that clamped it
    /// at zero, or replaced it with a flag, would delete the only signal BR-3 asks the system to
    /// produce.
    /// </summary>
    [Fact]
    public async Task ANegativeRemainingIsPassedThroughUnclamped()
    {
        _projectQueries.Seed(new ProjectInvoiceBalanceDto(7, 10_000.00m, 12_500.00m, -2_500.00m));

        var result = await _handler.HandleAsync(new GetProjectInvoiceBalanceQuery(7), CancellationToken.None);

        Assert.Equal(-2_500.00m, result.Remaining);
    }

    /// <summary>
    /// The DTO carries exactly the three fields Sequence Diagram §8 returns, plus the project id —
    /// no <c>isOverInvoiced</c>, no <c>warning</c>, no speculative flag. Pinned so one cannot drift
    /// in and quietly become a contract.
    /// </summary>
    [Fact]
    public void TheBalanceDtoCarriesNoWarningFlag()
    {
        var properties = typeof(ProjectInvoiceBalanceDto)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(
            [
                nameof(ProjectInvoiceBalanceDto.AgreedTotal),
                nameof(ProjectInvoiceBalanceDto.AlreadyInvoiced),
                nameof(ProjectInvoiceBalanceDto.ProjectId),
                nameof(ProjectInvoiceBalanceDto.Remaining),
            ],
            properties);
    }

    /// <summary>
    /// `PermissionMatrix.md` §5 grants this Admin <c>F</c> / Inspector <c>R</c> — read-only but
    /// **unscoped**, exactly like the Project detail read. A <c>RequestingInspectorId</c> appearing
    /// here would be a visible signature change to review against §5, not a predicate quietly added
    /// to a <c>WHERE</c> clause.
    /// </summary>
    [Fact]
    public void TheQueryCarriesNoScopeParameter()
    {
        var parameters = typeof(GetProjectInvoiceBalanceQuery)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.Name!)
            .ToArray();

        Assert.Equal([nameof(GetProjectInvoiceBalanceQuery.ProjectId)], parameters);
    }
}
