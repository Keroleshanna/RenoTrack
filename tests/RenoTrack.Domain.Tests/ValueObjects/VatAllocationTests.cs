using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Tests.ValueObjects;

/// <summary>
/// The externally important properties first: the totals reconcile exactly, BR-11 rounding holds,
/// the Angebot's rate mix is preserved subject to cent rounding, the result is deterministic, and a
/// zero target never divides. Which rate group absorbs a residual cent is rounding machinery and is
/// pinned only through those properties, not asserted as policy.
/// </summary>
public class VatAllocationTests
{
    private static VatBreakdownLine Line(VatRate rate, decimal net, decimal vat) =>
        new(rate, Money.FromExact(net), Money.FromExact(vat));

    /// <summary>A realistic mixed-rate Angebot: 19% and 7% together, as BR-6 requires to be possible.</summary>
    private static readonly VatBreakdownLine[] MixedRates =
    [
        Line(VatRate.Standard, 20_000.00m, 3_800.00m),
        Line(VatRate.Reduced, 1_000.00m, 70.00m),
    ];

    private static readonly VatBreakdownLine[] SingleStandardRate =
    [
        Line(VatRate.Standard, 10_000.00m, 1_900.00m),
    ];

    // ---- The invariant Invoice.Create re-checks -------------------------

    [Theory]
    [InlineData(8_000.00)]
    [InlineData(0.01)]
    [InlineData(12_345.67)]
    [InlineData(1.00)]
    [InlineData(24_870.00)]
    [InlineData(99_999.99)]
    public void NetPlusVatAlwaysEqualsTheRequestedGross(double requested)
    {
        var target = Money.FromExact((decimal)requested);

        var allocation = VatAllocation.ProportionalTo(MixedRates, target);

        Assert.Equal(target, allocation.GrossAmount);
        Assert.Equal(target, allocation.NetAmount + allocation.VatAmount);
    }

    /// <summary>
    /// The same property across many targets at once — the residual reconciliation must hold for
    /// every cent value, not only for the hand-picked ones above. A single cent lost or invented
    /// anywhere here would make <c>Invoice.Create</c> throw in production.
    /// </summary>
    [Fact]
    public void NetPlusVatEqualsGrossAcrossAContinuousRangeOfTargets()
    {
        for (var cents = 1; cents <= 2_000; cents++)
        {
            var target = Money.FromExact(cents / 100m);

            var allocation = VatAllocation.ProportionalTo(MixedRates, target);

            Assert.Equal(target, allocation.NetAmount + allocation.VatAmount);
        }
    }

    // ---- Proportionality and rounding ----------------------------------

    /// <summary>
    /// A single-rate Angebot is the case with an exactly checkable answer: 11,900 gross at 19%
    /// splits into 10,000.00 net and 1,900.00 VAT.
    /// </summary>
    [Fact]
    public void ASingleRateMixDerivesTheExactNetAndVat()
    {
        var allocation = VatAllocation.ProportionalTo(SingleStandardRate, Money.FromExact(11_900.00m));

        Assert.Equal(Money.FromExact(10_000.00m), allocation.NetAmount);
        Assert.Equal(Money.FromExact(1_900.00m), allocation.VatAmount);
    }

    /// <summary>
    /// Invoicing the Angebot's own gross in full must reproduce the Angebot's own net and VAT —
    /// the strongest statement of "consistent with the originating Angebot's rates" (FR-8.2).
    /// The mix here totals 21,000.00 net + 3,870.00 VAT = 24,870.00 gross.
    /// </summary>
    [Fact]
    public void AllocatingTheWholeAngebotGrossReproducesTheAngebotTotals()
    {
        var allocation = VatAllocation.ProportionalTo(MixedRates, Money.FromExact(24_870.00m));

        Assert.Equal(Money.FromExact(21_000.00m), allocation.NetAmount);
        Assert.Equal(Money.FromExact(3_870.00m), allocation.VatAmount);
    }

    /// <summary>
    /// Half the Angebot's gross must carry (within rounding) half its VAT — the proportionality
    /// FR-8.2 and Sequence Diagram §8 require. A blended-rate implementation would drift here.
    /// </summary>
    [Fact]
    public void HalfTheGrossCarriesHalfTheVatWithinACent()
    {
        var allocation = VatAllocation.ProportionalTo(MixedRates, Money.FromExact(12_435.00m));

        Assert.InRange(allocation.VatAmount.Amount, 1_934.99m, 1_935.01m);
        Assert.Equal(Money.FromExact(12_435.00m), allocation.NetAmount + allocation.VatAmount);
    }

    /// <summary>
    /// Every produced value is a valid <see cref="Money"/>, which is BR-11's two-decimal exactness
    /// by construction — <c>Money</c> refuses anything else at creation.
    /// </summary>
    [Fact]
    public void EveryAmountIsExactToTwoDecimalPlaces()
    {
        var allocation = VatAllocation.ProportionalTo(MixedRates, Money.FromExact(3_333.33m));

        Assert.Equal(decimal.Round(allocation.NetAmount.Amount, 2), allocation.NetAmount.Amount);
        Assert.Equal(decimal.Round(allocation.VatAmount.Amount, 2), allocation.VatAmount.Amount);
    }

    // ---- Determinism ----------------------------------------------------

    [Fact]
    public void TheSameInputAlwaysProducesTheSameResult()
    {
        var first = VatAllocation.ProportionalTo(MixedRates, Money.FromExact(7_777.77m));
        var second = VatAllocation.ProportionalTo(MixedRates, Money.FromExact(7_777.77m));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The result is a function of the rate mix itself, never of the order the caller happened to
    /// build the list in — the allocator sorts before it allocates.
    /// </summary>
    [Fact]
    public void TheOrderOfTheRateMixDoesNotChangeTheResult()
    {
        var ascending = VatAllocation.ProportionalTo(MixedRates, Money.FromExact(7_777.77m));
        var reversed = VatAllocation.ProportionalTo(MixedRates.Reverse().ToArray(), Money.FromExact(7_777.77m));

        Assert.Equal(ascending, reversed);
    }

    // ---- Zero handling (no division) ------------------------------------

    /// <summary>
    /// A zero target allocates to zero and — critically — takes that path *before* the mix's gross
    /// is used as a divisor, so a zero-gross Angebot cannot produce a division by zero here.
    /// </summary>
    [Fact]
    public void AZeroTargetAllocatesToZero()
    {
        var allocation = VatAllocation.ProportionalTo(MixedRates, Money.Zero);

        Assert.Equal(Money.Zero, allocation.NetAmount);
        Assert.Equal(Money.Zero, allocation.VatAmount);
        Assert.Equal(Money.Zero, allocation.GrossAmount);
    }

    /// <summary>
    /// Zero against zero: the case the Application layer deliberately still allows. It must not
    /// divide and must not throw — a zero-gross Invoice needs no proportion.
    /// </summary>
    [Fact]
    public void AZeroTargetAgainstAZeroGrossMixIsAllowedAndDoesNotDivide()
    {
        VatBreakdownLine[] zeroMix = [Line(VatRate.Standard, 0m, 0m)];

        var allocation = VatAllocation.ProportionalTo(zeroMix, Money.Zero);

        Assert.Equal(Money.Zero, allocation.NetAmount);
        Assert.Equal(Money.Zero, allocation.VatAmount);
    }

    [Fact]
    public void AZeroTargetAgainstAnEmptyMixIsAllowed()
    {
        var allocation = VatAllocation.ProportionalTo([], Money.Zero);

        Assert.Equal(Money.Zero, allocation.GrossAmount);
    }

    /// <summary>
    /// A positive target against a zero-gross mix has no proportion to allocate by. The Application
    /// layer rejects this with a <c>ConflictException</c> before reaching here; this is the backstop
    /// that makes the arithmetic path unreachable rather than merely unlikely.
    /// </summary>
    [Fact]
    public void APositiveTargetAgainstAZeroGrossMixIsRefused()
    {
        VatBreakdownLine[] zeroMix = [Line(VatRate.Standard, 0m, 0m)];

        var ex = Assert.Throws<ArgumentException>(
            () => VatAllocation.ProportionalTo(zeroMix, Money.FromExact(100.00m)));

        Assert.Equal("rateMix", ex.ParamName);
    }

    [Fact]
    public void APositiveTargetAgainstAnEmptyMixIsRefused()
    {
        Assert.Throws<ArgumentException>(() => VatAllocation.ProportionalTo([], Money.FromExact(100.00m)));
    }

    // ---- Guards ----------------------------------------------------------

    [Fact]
    public void ANegativeTargetIsRefused()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => VatAllocation.ProportionalTo(MixedRates, Money.FromExact(-1.00m)));

        Assert.Equal("targetGross", ex.ParamName);
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => VatAllocation.ProportionalTo(null!, Money.Zero));
        Assert.Throws<ArgumentNullException>(() => VatAllocation.ProportionalTo(MixedRates, null!));
    }

    /// <summary>
    /// A 0% rate group is legal (BR-6 lists 0% among the real rates) and must contribute net with
    /// no VAT. Here the mix is entirely zero-rated, so the whole allocation is net.
    /// </summary>
    [Fact]
    public void AZeroRatedMixProducesNoVat()
    {
        VatBreakdownLine[] zeroRated = [Line(VatRate.Zero, 5_000.00m, 0m)];

        var allocation = VatAllocation.ProportionalTo(zeroRated, Money.FromExact(1_234.56m));

        Assert.Equal(Money.FromExact(1_234.56m), allocation.NetAmount);
        Assert.Equal(Money.Zero, allocation.VatAmount);
    }

    /// <summary>
    /// Four distinct rates at once — the widest mix BR-6 permits — still reconciles exactly. This is
    /// where independent per-rate rounding is most likely to leave a residual.
    /// </summary>
    [Fact]
    public void AllFourRatesTogetherStillReconcile()
    {
        VatBreakdownLine[] allRates =
        [
            Line(VatRate.Zero, 100.00m, 0m),
            Line(VatRate.Reduced, 100.00m, 7.00m),
            Line(VatRate.Sixteen, 100.00m, 16.00m),
            Line(VatRate.Standard, 100.00m, 19.00m),
        ];

        for (var cents = 1; cents <= 500; cents++)
        {
            var target = Money.FromExact(cents / 100m);

            var allocation = VatAllocation.ProportionalTo(allRates, target);

            Assert.Equal(target, allocation.NetAmount + allocation.VatAmount);
        }
    }
}
