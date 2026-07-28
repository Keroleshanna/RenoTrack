using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Tests.ValueObjects;

public class ItemUnitTests
{
    [Theory]
    [InlineData("m2")]
    [InlineData("M2")]
    [InlineData("Stk")]
    [InlineData("STK")]
    [InlineData("pauschal")]
    public void Custom_RejectsLabelsThatCollideWithReservedCodes(string collidingLabel)
    {
        var act = () => ItemUnit.Custom(collidingLabel);

        Assert.Throws<ArgumentException>(act);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Custom_RejectsEmptyLabel(string emptyLabel)
    {
        var act = () => ItemUnit.Custom(emptyLabel);

        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Custom_AcceptsANonCollidingLabel()
    {
        var unit = ItemUnit.Custom("kg");

        Assert.Equal(UnitKind.Custom, unit.Kind);
        Assert.Equal("kg", unit.Code);
    }

    [Theory]
    [InlineData("m2", UnitKind.SquareMeter)]
    [InlineData("Stk", UnitKind.Piece)]
    [InlineData("lfm", UnitKind.LinearMeter)]
    [InlineData("pauschal", UnitKind.LumpSum)]
    [InlineData("m", UnitKind.Meter)]
    public void FromCode_RoundTripsStandardCodesToTheirStandardKind(string code, UnitKind expectedKind)
    {
        var unit = ItemUnit.FromCode(code);

        Assert.Equal(expectedKind, unit.Kind);
        Assert.Equal(code, unit.Code);
    }

    [Fact]
    public void FromCode_RoundTripsAnUnrecognizedCodeAsCustom()
    {
        var unit = ItemUnit.FromCode("kg");

        Assert.Equal(UnitKind.Custom, unit.Kind);
        Assert.Equal("kg", unit.CustomLabel);
    }

    [Fact]
    public void StandardFactories_ProduceValueEquality()
    {
        Assert.Equal(ItemUnit.SquareMeter(), ItemUnit.SquareMeter());
        Assert.NotEqual(ItemUnit.SquareMeter(), ItemUnit.Piece());
    }
}
