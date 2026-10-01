using System.Globalization;
using RenoTrack.Application.Common.Documents;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Common.Documents;

/// <summary>
/// Formatting is the least test-visible part of a document and the most likely to be quietly wrong
/// (CLAUDE.md §24's rule for the Website, applied to paper — D111).
/// </summary>
public class DocumentFormattingTests
{
    [Theory]
    [InlineData(0, "0,00 €")]
    [InlineData(0.01, "0,01 €")]
    [InlineData(950, "950,00 €")]
    [InlineData(1234.56, "1.234,56 €")]
    [InlineData(1234567.89, "1.234.567,89 €")]
    public void MoneyIsGermanWithTwoDecimals(double amount, string expected)
    {
        Assert.Equal(expected, DocumentFormatting.Money((decimal)amount));
    }

    [Theory]
    [InlineData(VatRate.Zero, "0 %")]
    [InlineData(VatRate.Reduced, "7 %")]
    [InlineData(VatRate.Sixteen, "16 %")]
    [InlineData(VatRate.Standard, "19 %")]
    public void VatRatesArePrintedAsPercentages(VatRate rate, string expected)
    {
        Assert.Equal(expected, DocumentFormatting.VatRate(rate));
    }

    /// <summary>
    /// An explicit de-DE culture, never the ambient one: a server set to en-US must still print a
    /// German invoice.
    /// </summary>
    [Fact]
    public void TheAmbientCultureIsIgnored()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

            Assert.Equal("1.234,56 €", DocumentFormatting.Money(1234.56m));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
