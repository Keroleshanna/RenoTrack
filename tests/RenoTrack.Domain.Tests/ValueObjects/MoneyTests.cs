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
