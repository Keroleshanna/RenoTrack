using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Proves NumberGeneratorService's atomic increment-and-return strategy (D52) against real
/// LocalDB — including, critically, the concurrency claim itself: many parallel callers, each
/// with its own DbContext (a DbContext is not thread-safe, so this genuinely exercises SQL
/// Server's own row locking, not just C# code), must never receive the same number.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class NumberGeneratorServiceTests(RenoTrackDbContextFixture fixture)
{
    [Fact]
    public async Task NextAngebotNumberAsync_ForANewYear_ReturnsSequenceOne()
    {
        var year = UniqueYear();
        await using var context = fixture.CreateContext();
        var service = new NumberGeneratorService(context);

        var number = await service.NextAngebotNumberAsync(year, CancellationToken.None);

        Assert.Equal($"ANG-{year}-00001", number);
    }

    [Fact]
    public async Task NextAngebotNumberAsync_CalledTwiceSequentially_Increments()
    {
        var year = UniqueYear();
        await using var context = fixture.CreateContext();
        var service = new NumberGeneratorService(context);

        var first = await service.NextAngebotNumberAsync(year, CancellationToken.None);
        var second = await service.NextAngebotNumberAsync(year, CancellationToken.None);

        Assert.Equal($"ANG-{year}-00001", first);
        Assert.Equal($"ANG-{year}-00002", second);
    }

    [Fact]
    public async Task NextAngebotNumberAsync_DifferentYears_EachStartsItsOwnSequenceAtOne()
    {
        var yearA = UniqueYear();
        var yearB = UniqueYear();
        await using var context = fixture.CreateContext();
        var service = new NumberGeneratorService(context);

        var numberA = await service.NextAngebotNumberAsync(yearA, CancellationToken.None);
        var numberB = await service.NextAngebotNumberAsync(yearB, CancellationToken.None);

        Assert.Equal($"ANG-{yearA}-00001", numberA);
        Assert.Equal($"ANG-{yearB}-00001", numberB);
    }

    [Fact]
    public async Task NextAngebotNumberAsync_ManyConcurrentCallsForTheSameYear_NeverReturnsADuplicate()
    {
        // The real concurrency proof: 50 parallel callers, each with its own DbContext (a
        // DbContext is not thread-safe — sharing one across Task.WhenAll would be a test bug,
        // not a real concurrency test), racing for the same year, including the very first
        // request for that year (exercising the insert-or-retry first-of-year path too).
        var year = UniqueYear();
        const int concurrentRequests = 50;

        var tasks = Enumerable.Range(0, concurrentRequests).Select(async _ =>
        {
            await using var context = fixture.CreateContext();
            var service = new NumberGeneratorService(context);
            return await service.NextAngebotNumberAsync(year, CancellationToken.None);
        });

        var results = await Task.WhenAll(tasks);

        Assert.Equal(concurrentRequests, results.Length);
        Assert.Equal(concurrentRequests, results.Distinct().Count());
        var expectedNumbers = Enumerable.Range(1, concurrentRequests).Select(n => $"ANG-{year}-{n:D5}");
        Assert.Equal(expectedNumbers.OrderBy(x => x), results.OrderBy(x => x));
    }

    /// <summary>
    /// Each test uses its own synthetic year so tests never collide with each other despite
    /// sharing one LocalDB database (the "Infrastructure Database" collection runs serially,
    /// but distinct years keep assertions independent and easy to reason about regardless).
    /// </summary>
    private static int UniqueYear() => Random.Shared.Next(100_000, 999_999);
}
