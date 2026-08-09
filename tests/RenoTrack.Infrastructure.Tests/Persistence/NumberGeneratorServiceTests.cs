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
    // ---- Invoice numbers (Phase 8 Slice 3) ------------------------------

    [Fact]
    public async Task NextInvoiceNumberAsync_ForANewYear_ReturnsSequenceOne()
    {
        var year = UniqueYear();
        await using var context = fixture.CreateContext();
        var service = new NumberGeneratorService(context);

        var number = await service.NextInvoiceNumberAsync(year, CancellationToken.None);

        Assert.Equal($"RE-{year}-00001", number);
    }

    [Fact]
    public async Task NextInvoiceNumberAsync_CalledTwiceSequentially_Increments()
    {
        var year = UniqueYear();
        await using var context = fixture.CreateContext();
        var service = new NumberGeneratorService(context);

        var first = await service.NextInvoiceNumberAsync(year, CancellationToken.None);
        var second = await service.NextInvoiceNumberAsync(year, CancellationToken.None);

        Assert.Equal($"RE-{year}-00001", first);
        Assert.Equal($"RE-{year}-00002", second);
    }

    /// <summary>
    /// Invoice and Angebot numbering share the machinery but not the counter — ERD.md keys
    /// NumberSequences on (SequenceType, Year), so the same year must start both at 1 independently.
    /// A shared counter would make invoice numbering skip whenever an Angebot was created.
    /// </summary>
    [Fact]
    public async Task InvoiceAndAngebotSequencesAreIndependentWithinTheSameYear()
    {
        var year = UniqueYear();
        await using var context = fixture.CreateContext();
        var service = new NumberGeneratorService(context);

        var angebot = await service.NextAngebotNumberAsync(year, CancellationToken.None);
        var invoice = await service.NextInvoiceNumberAsync(year, CancellationToken.None);
        var secondAngebot = await service.NextAngebotNumberAsync(year, CancellationToken.None);

        Assert.Equal($"ANG-{year}-00001", angebot);
        Assert.Equal($"RE-{year}-00001", invoice);
        Assert.Equal($"ANG-{year}-00002", secondAngebot);
    }

    /// <summary>
    /// BR-9 — an invoice number is never reused — is only as good as this holds under concurrency.
    /// Each caller gets its own DbContext (a DbContext is not thread-safe), so this exercises SQL
    /// Server row locking rather than C# sequencing, exactly as the Angebot equivalent does.
    /// </summary>
    [Fact]
    public async Task NextInvoiceNumberAsync_UnderParallelLoad_NeverRepeatsANumber()
    {
        var year = UniqueYear();
        const int callers = 50;

        var numbers = await Task.WhenAll(Enumerable.Range(0, callers).Select(async _ =>
        {
            await using var context = fixture.CreateContext();
            return await new NumberGeneratorService(context).NextInvoiceNumberAsync(year, CancellationToken.None);
        }));

        Assert.Equal(callers, numbers.Distinct().Count());
    }

    private static int UniqueYear() => Random.Shared.Next(100_000, 999_999);
}
