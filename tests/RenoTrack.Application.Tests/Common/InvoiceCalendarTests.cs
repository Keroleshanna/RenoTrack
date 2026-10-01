using RenoTrack.Application.Common;

namespace RenoTrack.Application.Tests.Common;

/// <summary>
/// D111 Part 6: an invoice's date and number year are its issue instant read in Europe/Berlin. The
/// interesting instants are the ones where UTC and Berlin disagree about the day — the hours either
/// side of local midnight, New Year's Eve, and both daylight-saving changes.
/// </summary>
public class InvoiceCalendarTests
{
    private static readonly InvoiceCalendar Calendar = InvoiceCalendar.ForEuropeBerlin();

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void TheZoneIsEuropeBerlin()
    {
        Assert.Equal("Europe/Berlin", InvoiceCalendar.TimeZoneId);
    }

    /// <summary>22:30 UTC on 1 October is 00:30 on 2 October in Berlin (summer time, UTC+2).</summary>
    [Fact]
    public void AnInstantAfterBerlinMidnightInSummerIsTheNextDay()
    {
        Assert.Equal(new DateOnly(2026, 10, 2), Calendar.DateOf(Utc(2026, 10, 1, 22, 30)));
    }

    /// <summary>21:59 UTC on 1 October is 23:59 on 1 October in Berlin — still the same day.</summary>
    [Fact]
    public void AnInstantBeforeBerlinMidnightInSummerIsTheSameDay()
    {
        Assert.Equal(new DateOnly(2026, 10, 1), Calendar.DateOf(Utc(2026, 10, 1, 21, 59)));
    }

    /// <summary>
    /// 23:30 UTC on 31 December is 00:30 on 1 January in Berlin (winter time, UTC+1): the next day
    /// <i>and</i> the next year — which is what numbers the invoice in the new year.
    /// </summary>
    [Fact]
    public void NewYearsEveAfterBerlinMidnightIsTheFirstOfJanuaryOfTheNextYear()
    {
        var instant = Utc(2026, 12, 31, 23, 30);

        Assert.Equal(new DateOnly(2027, 1, 1), Calendar.DateOf(instant));
        Assert.Equal(2027, Calendar.YearOf(instant));
    }

    /// <summary>22:59 UTC on 31 December is 23:59 in Berlin — still the old year.</summary>
    [Fact]
    public void NewYearsEveBeforeBerlinMidnightIsStillTheOldYear()
    {
        var instant = Utc(2026, 12, 31, 22, 59);

        Assert.Equal(new DateOnly(2026, 12, 31), Calendar.DateOf(instant));
        Assert.Equal(2026, Calendar.YearOf(instant));
    }

    /// <summary>
    /// Spring forward, 29 March 2026: Berlin's offset changes from +1 to +2 at 01:00 UTC. Midnight
    /// that night is still on +1, so 22:59/23:00 UTC on 28 March are 23:59/00:00 local; and the jump
    /// itself does not move the date.
    /// </summary>
    [Theory]
    [InlineData(28, 22, 59, 28)]
    [InlineData(28, 23, 0, 29)]
    [InlineData(29, 0, 59, 29)]
    [InlineData(29, 1, 0, 29)]
    [InlineData(29, 21, 59, 29)]
    [InlineData(29, 22, 0, 30)]
    public void TheSpringDaylightSavingChangeDatesCorrectly(int utcDay, int hour, int minute, int expectedDay)
    {
        Assert.Equal(new DateOnly(2026, 3, expectedDay), Calendar.DateOf(Utc(2026, 3, utcDay, hour, minute)));
    }

    /// <summary>
    /// Fall back, 25 October 2026: Berlin's offset changes from +2 to +1 at 01:00 UTC. Midnight
    /// before it is on +2 (22:00 UTC on 24 October); midnight after it is on +1 (23:00 UTC on 25th).
    /// </summary>
    [Theory]
    [InlineData(24, 21, 59, 24)]
    [InlineData(24, 22, 0, 25)]
    [InlineData(25, 0, 30, 25)]
    [InlineData(25, 1, 30, 25)]
    [InlineData(25, 22, 59, 25)]
    [InlineData(25, 23, 0, 26)]
    public void TheAutumnDaylightSavingChangeDatesCorrectly(int utcDay, int hour, int minute, int expectedDay)
    {
        Assert.Equal(new DateOnly(2026, 10, expectedDay), Calendar.DateOf(Utc(2026, 10, utcDay, hour, minute)));
    }

    /// <summary>
    /// SQL Server's <c>datetime2</c> keeps no kind, so a stored issue instant comes back
    /// <see cref="DateTimeKind.Unspecified"/>. It must be read as the UTC it was written as.
    /// </summary>
    [Fact]
    public void ADatabaseLoadedUnspecifiedValueIsReadAsUtc()
    {
        var loaded = new DateTime(2026, 12, 31, 23, 30, 0, DateTimeKind.Unspecified);

        Assert.Equal(new DateOnly(2027, 1, 1), Calendar.DateOf(loaded));
        Assert.Equal(Calendar.DateOf(DateTime.SpecifyKind(loaded, DateTimeKind.Utc)), Calendar.DateOf(loaded));
    }

    /// <summary>A local time is not an instant this system stores, and its meaning varies by host.</summary>
    [Fact]
    public void ALocalTimeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Calendar.DateOf(new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Local)));
    }

    /// <summary>
    /// Fail fast: a host without the zone's data refuses rather than dating invoices wrongly. The
    /// message names the zone, so an operator knows what to install.
    /// </summary>
    [Fact]
    public void AZoneTheHostCannotResolveFailsNamingIt()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => InvoiceCalendar.ForZone("Nowhere/Not_A_Zone"));

        Assert.Contains("Nowhere/Not_A_Zone", ex.Message);
        Assert.Contains("tzdata", ex.Message);
    }

    [Fact]
    public void EuropeBerlinResolvesOnThisHost()
    {
        Assert.Equal(new DateOnly(2026, 6, 1), InvoiceCalendar.ForEuropeBerlin().DateOf(Utc(2026, 6, 1, 12, 0)));
    }
}
