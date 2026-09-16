using System.Net.Mail;
using System.Text.RegularExpressions;

namespace RenoTrack.Website.Content;

/// <summary>
/// The one accepted form of the company's phone number and email address.
/// </summary>
/// <remarks>
/// <para>
/// <b>One stored spelling, every other form derived.</b> The phone number is shown as written, turned
/// into a <c>tel:</c> link by removing spaces, and — in a later slice — published as structured data.
/// Accepting a national form (<c>0151 …</c>) as well would mean two spellings of one fact, and a
/// <c>tel:</c> link that only works inside one country.
/// </para>
/// <para>
/// Applies to every deployment (Slice 1 decision S1-3). An absent value is not this type's concern.
/// </para>
/// </remarks>
internal static partial class ContactFormats
{
    internal const int MinPhoneDigits = 8;
    internal const int MaxPhoneDigits = 15;

    internal static void ValidatePhone(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var digits = value.Count(char.IsAsciiDigit);

        if (!InternationalPhonePattern().IsMatch(value) || digits < MinPhoneDigits || digits > MaxPhoneDigits)
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' must be an international phone number: '+', the country code, then " +
                $"digit groups separated by single spaces, {MinPhoneDigits}–{MaxPhoneDigits} digits in total, " +
                "e.g. '+49 30 1234567'.");
        }
    }

    internal static void ValidateEmail(string? value, string key, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // MailAddress also accepts "Name <address>" and quoted forms; requiring the parsed address to be
        // the whole value refuses both, so a mailto: link and a displayed address can only ever be the
        // same plain string.
        var isPlainAddress = value.Length <= maxLength
            && !value.Any(char.IsWhiteSpace)
            && !value.Contains(',', StringComparison.Ordinal)
            && !value.Contains(';', StringComparison.Ordinal)
            && MailAddress.TryCreate(value, out var parsed)
            && string.Equals(parsed.Address, value, StringComparison.Ordinal);

        if (!isPlainAddress)
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' must be exactly one plain email address, e.g. 'kontakt@example.test', " +
                $"with no display name and at most {maxLength} characters.");
        }
    }

    [GeneratedRegex("^\\+[1-9][0-9]*( [0-9]+)*$")]
    private static partial Regex InternationalPhonePattern();
}
