using System.Globalization;
using RenoTrack.Website.Content;

namespace RenoTrack.Website.Site;

/// <summary>One line of opening hours as the footer shows it.</summary>
/// <param name="DaysShort">e.g. <c>Mo–Fr</c> or <c>Mo, Mi</c>.</param>
/// <param name="DaysLong">The same days written out, e.g. <c>Montag bis Freitag</c>, for an <c>abbr</c> title.</param>
/// <param name="Times">e.g. <c>08:00–18:00 Uhr</c>.</param>
public sealed record OpeningHoursLine(string DaysShort, string DaysLong, string Times);

/// <summary>
/// Presentation of company facts on the marketing site — German, and tested on its own, because formatting
/// is the least test-visible part of a rendered page (<c>CLAUDE.md</c> §24's <c>CustomerFormatting</c> rule).
/// </summary>
public static class SiteFormatting
{
    private const string EnDash = "–";

    /// <summary>Monday first, as a German week is read.</summary>
    private static readonly DayOfWeek[] WeekOrder =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    private static readonly Dictionary<DayOfWeek, (string Short, string Long)> DayNames = new()
    {
        [DayOfWeek.Monday] = ("Mo", "Montag"),
        [DayOfWeek.Tuesday] = ("Di", "Dienstag"),
        [DayOfWeek.Wednesday] = ("Mi", "Mittwoch"),
        [DayOfWeek.Thursday] = ("Do", "Donnerstag"),
        [DayOfWeek.Friday] = ("Fr", "Freitag"),
        [DayOfWeek.Saturday] = ("Sa", "Samstag"),
        [DayOfWeek.Sunday] = ("So", "Sonntag"),
    };

    /// <summary>
    /// Each configured block as one line, ordered by its first day. Three or more consecutive days become a
    /// range (<c>Mo–Fr</c>); anything else is listed (<c>Mo, Mi, Fr</c>, <c>Sa, So</c>).
    /// </summary>
    public static IReadOnlyList<OpeningHoursLine> OpeningHours(IEnumerable<OpeningHoursOptions> blocks) =>
        blocks
            .Select(block => (Days: block.DaysOfWeek.OrderBy(Position).ToList(), block.Opens, block.Closes))
            .OrderBy(block => Position(block.Days[0]))
            .Select(block =>
            {
                var (daysShort, daysLong) = Days(block.Days);
                return new OpeningHoursLine(daysShort, daysLong, $"{block.Opens!.Trim()}{EnDash}{block.Closes!.Trim()} Uhr");
            })
            .ToList();

    /// <summary>A German list: <c>A</c>, <c>A und B</c>, <c>A, B und C</c>.</summary>
    public static string JoinGerman(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} und {items[^1]}",
    };

    /// <summary>The configured place names, in configured order, as one German list.</summary>
    public static string ServiceAreaPlaces(ServiceAreaOptions area) =>
        JoinGerman(area.Places.Select(place => place.Name!.Trim()).ToList());

    /// <summary>
    /// A <c>tel:</c> link from the one stored international spelling: <c>+49 000 1234567</c> becomes
    /// <c>tel:+490001234567</c>.
    /// </summary>
    public static string TelHref(string phone) =>
        "tel:+" + new string(phone.Where(char.IsAsciiDigit).ToArray());

    /// <summary>A <c>mailto:</c> link. The address is already validated as one plain address.</summary>
    public static string MailtoHref(string email) => $"mailto:{email.Trim()}";

    /// <summary>
    /// A <c>mailto:</c> link with a subject, or without one when <paramref name="subject"/> is null. The subject is
    /// percent-encoded whole, so company text containing <c>&amp;</c>, <c>?</c>, <c>#</c> or <c>%</c> can never add
    /// a <c>body</c>, <c>cc</c> or any other field (D105).
    /// </summary>
    public static string MailtoHref(string email, string? subject) =>
        subject is null ? MailtoHref(email) : $"{MailtoHref(email)}?subject={Uri.EscapeDataString(subject)}";

    /// <summary>The subject of an email started from a service page, e.g. <c>Anfrage: Innenausbau</c> (D105).</summary>
    public static string ServiceMailSubject(ServiceOptions service) => $"Anfrage: {service.Name!.Trim()}";

    /// <summary>The year for the copyright line, taken in UTC.</summary>
    public static string CopyrightYear(DateTimeOffset now) => now.UtcDateTime.Year.ToString(CultureInfo.InvariantCulture);

    private static int Position(DayOfWeek day) => Array.IndexOf(WeekOrder, day);

    private static (string Short, string Long) Days(IReadOnlyList<DayOfWeek> days)
    {
        var positions = days.Select(Position).ToList();
        var consecutive = positions.Zip(positions.Skip(1), (left, right) => right - left).All(step => step == 1);

        if (days.Count >= 3 && consecutive)
        {
            return ($"{DayNames[days[0]].Short}{EnDash}{DayNames[days[^1]].Short}",
                $"{DayNames[days[0]].Long} bis {DayNames[days[^1]].Long}");
        }

        return (string.Join(", ", days.Select(day => DayNames[day].Short)),
            JoinGerman(days.Select(day => DayNames[day].Long).ToList()));
    }
}
