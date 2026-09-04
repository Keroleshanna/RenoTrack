using System.Globalization;
using RenoTrack.Website.Rendering;

namespace RenoTrack.Website.Tests.Rendering;

/// <summary>
/// Formatting is the least test-visible part of a rendered page and the most likely to be quietly
/// wrong, which is why it lives in its own class and is tested directly rather than only through
/// the page.
/// </summary>
public sealed class CustomerFormattingTests
{
    /// <summary>
    /// Runs an assertion with the ambient culture set to something that is *not* German, so a
    /// method that forgot its explicit culture produces "1,234.56" and fails here.
    /// </summary>
    private static void UnderAmbientCulture(string cultureName, Action assert)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            assert();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ---- Money -------------------------------------------------------------

    [Theory]
    [InlineData(0, "0,00 €")]
    [InlineData(5, "5,00 €")]
    [InlineData(0.75, "0,75 €")]
    [InlineData(1234.56, "1.234,56 €")]
    [InlineData(1234567.89, "1.234.567,89 €")]
    public void Money_is_written_the_german_way(decimal amount, string expected)
    {
        Assert.Equal(expected, CustomerFormatting.Money(amount));
    }

    /// <summary>
    /// The defect this guards against is invisible on a developer's machine and silent in
    /// production: a server under another locale rendering "1,234.56" to a German customer.
    /// </summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("")]
    public void Money_ignores_the_ambient_culture(string ambient)
    {
        UnderAmbientCulture(ambient, () =>
            Assert.Equal("1.234,56 €", CustomerFormatting.Money(1234.56m)));
    }

    // ---- Quantity ----------------------------------------------------------

    /// <summary>
    /// Q3: up to two decimals, trailing zeros trimmed. A quantity is a count or a measurement and
    /// reads naturally without forced decimals — "10 m²" rather than "10,00 m²" — unlike money,
    /// where a price written as "5 €" reads as an approximation.
    /// </summary>
    [Theory]
    [InlineData(10, "10")]
    [InlineData(10.00, "10")]
    [InlineData(2.5, "2,5")]
    [InlineData(2.50, "2,5")]
    [InlineData(0.75, "0,75")]
    [InlineData(0, "0")]
    [InlineData(1250, "1250")]
    public void Quantity_trims_trailing_zeros_and_uses_a_german_decimal_separator(decimal quantity, string expected)
    {
        Assert.Equal(expected, CustomerFormatting.Quantity(quantity));
    }

    [Fact]
    public void Quantity_ignores_the_ambient_culture()
    {
        UnderAmbientCulture("en-US", () => Assert.Equal("2,5", CustomerFormatting.Quantity(2.5m)));
    }

    // ---- VAT rate ----------------------------------------------------------

    [Theory]
    [InlineData(0, "0")]
    [InlineData(7, "7")]
    [InlineData(16, "16")]
    [InlineData(19, "19")]
    public void A_vat_rate_reads_as_a_whole_percentage(decimal rate, string expected)
    {
        Assert.Equal(expected, CustomerFormatting.VatRate(rate));
    }

    // ---- Unit --------------------------------------------------------------

    /// <summary>
    /// <c>m2</c> is the one standard code whose storage form is an ASCII compromise rather than how
    /// it is written.
    /// </summary>
    [Fact]
    public void The_square_metre_code_is_written_as_a_square_metre()
    {
        Assert.Equal("m²", CustomerFormatting.Unit("m2"));
    }

    /// <summary>
    /// <c>ItemUnit</c> is an open value object, so an unrecognised code is a legitimate custom unit
    /// an Inspector typed. It must pass through untouched rather than being mapped, normalised or
    /// dropped — the same rule `CLAUDE.md` §23 records for the Dashboard's unit control.
    /// </summary>
    [Theory]
    [InlineData("Stk")]
    [InlineData("lfm")]
    [InlineData("pauschal")]
    [InlineData("m")]
    [InlineData("Rolle")]
    [InlineData("M2")]
    [InlineData("Sack Zement")]
    public void Every_other_unit_passes_through_unchanged(string unitCode)
    {
        Assert.Equal(unitCode, CustomerFormatting.Unit(unitCode));
    }

    // ---- Position numbers --------------------------------------------------

    /// <summary>
    /// Wireframe A3's form: section 1's third line is <c>1.003</c>. Derived from position, which is
    /// only meaningful because the projection's item order is deterministic.
    /// </summary>
    [Theory]
    [InlineData(0, 0, "1.001")]
    [InlineData(0, 2, "1.003")]
    [InlineData(1, 0, "2.001")]
    [InlineData(2, 41, "3.042")]
    [InlineData(0, 999, "1.1000")]
    public void A_position_number_pairs_the_section_with_the_line(int sectionIndex, int itemIndex, string expected)
    {
        Assert.Equal(expected, CustomerFormatting.PositionNumber(sectionIndex, itemIndex));
    }

    [Theory]
    [InlineData(0, "1")]
    [InlineData(4, "5")]
    public void A_section_number_is_one_based(int sectionIndex, string expected)
    {
        Assert.Equal(expected, CustomerFormatting.SectionNumber(sectionIndex));
    }

    /// <summary>
    /// Position numbers are structural, not localised — they must not pick up a thousands separator
    /// from any culture.
    /// </summary>
    [Fact]
    public void Position_numbers_ignore_the_ambient_culture()
    {
        UnderAmbientCulture("de-DE", () =>
        {
            Assert.Equal("1.001", CustomerFormatting.PositionNumber(0, 0));
            Assert.Equal("5", CustomerFormatting.SectionNumber(4));
        });
    }
}
