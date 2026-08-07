using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Tests.ValueObjects;

public class MoneyTests
{
    // ---- FromExact --------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(10.50)]
    [InlineData(-5.25)]
    [InlineData(1234567.89)]
    public void FromExact_AcceptsValuesAlreadyExactToTwoDecimals(double value)
    {
        var amount = (decimal)value;

        var money = Money.FromExact(amount);

        Assert.Equal(amount, money.Amount);
    }

    [Theory]
    [InlineData(10.505)]
    [InlineData(0.001)]
    [InlineData(1.999)]
    [InlineData(-3.333)]
    public void FromExact_RejectsValuesWithMoreThanTwoDecimalPlaces(double value)
    {
        var amount = (decimal)value;

        Assert.Throws<ArgumentException>(() => Money.FromExact(amount));
    }

    // ---- RoundedPerBR11 (BR-11: MidpointRounding.AwayFromZero) ------------

    [Theory]
    [InlineData(255.5112, 255.51)]     // typical Quantity x UnitPrice result, rounds down
    [InlineData(255.5150, 255.52)]     // midpoint at the 2nd decimal, positive — away from zero rounds up
    [InlineData(0.125, 0.13)]          // midpoint, positive
    [InlineData(-0.125, -0.13)]        // midpoint, negative — away from zero rounds further negative
    [InlineData(1.005, 1.01)]          // midpoint, positive
    [InlineData(-1.005, -1.01)]        // midpoint, negative
    [InlineData(-10.567, -10.57)]      // ordinary negative value
    [InlineData(0, 0)]                 // zero
    public void RoundedPerBR11_RoundsAwayFromZero(double raw, double expected)
    {
        var money = Money.RoundedPerBR11((decimal)raw);

        Assert.Equal((decimal)expected, money.Amount);
    }

    // ---- Zero ---------------------------------------------------------

    [Fact]
    public void Zero_IsZeroAmount()
    {
        Assert.Equal(0m, Money.Zero.Amount);
    }

    // ---- + and Sum ------------------------------------------------------

    [Fact]
    public void Addition_SumsAmountsWithoutReRounding()
    {
        var result = Money.FromExact(1.10m) + Money.FromExact(2.20m);

        Assert.Equal(Money.FromExact(3.30m), result);
    }

    [Fact]
    public void Addition_HandlesNegativeAmounts()
    {
        var result = Money.FromExact(5.00m) + Money.FromExact(-2.50m);

        Assert.Equal(Money.FromExact(2.50m), result);
    }

    // ---- - (Phase 8, BR-3's remaining balance) --------------------------

    [Fact]
    public void Subtraction_SubtractsAmountsWithoutReRounding()
    {
        var result = Money.FromExact(25_673.36m) - Money.FromExact(8_000.00m);

        Assert.Equal(Money.FromExact(17_673.36m), result);
    }

    /// <summary>
    /// BR-3 warns rather than blocks when invoices exceed the agreed total, so an over-invoiced
    /// Project's remaining balance is negative — and that negative value *is* the warning. Clamping
    /// it would hide the data-entry mistake BR-3 exists to catch.
    /// </summary>
    [Fact]
    public void Subtraction_ProducesNegativeWhenTheSubtrahendIsLarger()
    {
        var result = Money.FromExact(8_000.00m) - Money.FromExact(8_250.00m);

        Assert.Equal(Money.FromExact(-250.00m), result);
    }

    [Fact]
    public void Subtraction_OfEqualAmounts_IsZero()
    {
        var result = Money.FromExact(1_234.56m) - Money.FromExact(1_234.56m);

        Assert.Equal(Money.Zero, result);
    }

    /// <summary>
    /// Subtraction is the exact inverse of addition here precisely because neither re-rounds — the
    /// property that makes a remaining-balance figure reconcile against the invoices behind it.
    /// </summary>
    [Fact]
    public void Subtraction_InvertsAdditionExactly()
    {
        var original = Money.FromExact(999.99m);
        var delta = Money.FromExact(0.07m);

        Assert.Equal(original, original + delta - delta);
    }

    [Fact]
    public void Sum_OfEmptyCollection_IsZero()
    {
        var result = Money.Sum([]);

        Assert.Equal(Money.Zero, result);
    }

    [Fact]
    public void Sum_OfSingleElement_EqualsThatElement()
    {
        var single = Money.FromExact(42.42m);

        var result = Money.Sum([single]);

        Assert.Equal(single, result);
    }

    [Fact]
    public void Sum_OfMultipleElements_AddsAllAmounts()
    {
        var values = new[] { Money.FromExact(10.00m), Money.FromExact(5.25m), Money.FromExact(0.75m) };

        var result = Money.Sum(values);

        Assert.Equal(Money.FromExact(16.00m), result);
    }
}
