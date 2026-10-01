namespace RenoTrack.Application.Common;

/// <summary>
/// Turns the UTC instant an Invoice was issued into the calendar date and year it carries
/// (Phase 14 Slice 2, <b>D111 Part 6</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope: the Invoice's issue date and its number year — nothing else.</b> An invoice is a German
/// legal document (BR-5, §14 UStG), and its date is the date in Germany: an invoice created at 00:30
/// on 1 January in Berlin is dated 1 January and numbered in the new year, although the instant is
/// still 31 December in UTC. The zone is therefore fixed to <see cref="TimeZoneId"/> by decision,
/// with no configuration. <b>This is not an application-wide time-zone policy.</b> The Angebot number
/// year, the overdue/receivables "today" and date serialisation elsewhere still use UTC, and are
/// recorded as separate future work (<c>NEXT_STEPS.md</c> §8e).
/// </para>
/// <para>
/// <b>The stored value stays the exact UTC instant.</b> The calendar date is derived from it, the
/// same way every time, so no column changed and no migration was needed.
/// </para>
/// <para>
/// <b>Resolved once, at startup, and fails fast.</b> <see cref="ForEuropeBerlin"/> is called while
/// <c>AddApplication()</c> composes the container, so a host without time-zone data (some minimal
/// Linux images ship none) refuses to start, naming the zone — rather than failing on the first
/// invoice someone creates. That makes the zone data a deployment prerequisite.
/// </para>
/// <para>
/// It lives in <c>Application.Common</c> because both the invoice handler and the document assembler
/// use it, and Common never depends on a feature folder. Like <c>OwnershipValidator</c> it has no
/// external dependency — <see cref="TimeZoneInfo"/> is the BCL — so it needs no Infrastructure side.
/// </para>
/// </remarks>
public sealed class InvoiceCalendar
{
    /// <summary>The IANA zone an invoice's date and number year are read in. Fixed by D111 Part 6.</summary>
    public const string TimeZoneId = "Europe/Berlin";

    private readonly TimeZoneInfo _zone;

    private InvoiceCalendar(TimeZoneInfo zone) => _zone = zone;

    /// <summary>Resolves <see cref="TimeZoneId"/> from the host's time-zone data.</summary>
    /// <exception cref="InvalidOperationException">The host has no data for the zone.</exception>
    public static InvoiceCalendar ForEuropeBerlin() => ForZone(TimeZoneId);

    /// <summary>
    /// The resolution itself, separated only so the failure path is testable with a zone that does
    /// not exist. Production calls <see cref="ForEuropeBerlin"/> and nothing else.
    /// </summary>
    internal static InvoiceCalendar ForZone(string timeZoneId)
    {
        try
        {
            return new InvoiceCalendar(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"The time zone '{timeZoneId}' could not be resolved on this host. Invoice dates and invoice-number "
                + "years are read in this zone (ARCHITECTURE_DECISIONS.md D111), so the application refuses to start "
                + "without it. Install the operating system's time-zone data (e.g. the 'tzdata' package).",
                ex);
        }
    }

    /// <summary>
    /// The calendar date, in <see cref="TimeZoneId"/>, of a UTC instant.
    /// </summary>
    /// <param name="utcInstant">
    /// A UTC instant. <see cref="DateTimeKind.Unspecified"/> is accepted and read as UTC, because that
    /// is what SQL Server's <c>datetime2</c> gives back for a value written as UTC. A local time is
    /// refused: it is not an instant this system ever stores.
    /// </param>
    /// <exception cref="ArgumentException">The value is a local time.</exception>
    public DateOnly DateOf(DateTime utcInstant)
    {
        if (utcInstant.Kind == DateTimeKind.Local)
        {
            throw new ArgumentException(
                "An invoice instant must be UTC; a local time is ambiguous across hosts.", nameof(utcInstant));
        }

        var utc = DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, _zone));
    }

    /// <summary>The calendar year, in <see cref="TimeZoneId"/>, of a UTC instant — the invoice-number year.</summary>
    public int YearOf(DateTime utcInstant) => DateOf(utcInstant).Year;
}
