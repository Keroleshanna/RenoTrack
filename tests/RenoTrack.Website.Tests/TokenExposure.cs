using System.Text.RegularExpressions;

namespace RenoTrack.Website.Tests;

/// <summary>
/// Where a customer's link token is allowed to appear in a rendered page, and where it is not.
/// </summary>
/// <remarks>
/// <para>
/// <b>Slices 2 and 3 asserted the token appeared nowhere at all, and Slice 4 made that impossible
/// rather than merely inconvenient.</b> The decision routes live under the token
/// (<c>/angebot/{token}/entscheidung/{choice}</c>), which is what keeps the credential in the route
/// and out of a hidden field, a query string and the request body — the Product Owner's own
/// requirement. A link to such a route necessarily contains it.
/// </para>
/// <para>
/// <b>So the rule is narrowed, not dropped, and this type is what enforces the narrower version.</b>
/// The token may appear only inside the <c>href</c> of a same-origin link within the customer's own
/// flow. It must never appear in visible text, in an <c>input</c> of any kind, in a query string, or
/// in anything sent to a third party. That is safe for reasons that do not hold for the excluded
/// places: the browser is already on a URL containing the token, so a link to a sibling route tells
/// it nothing new; <c>Referrer-Policy: no-referrer</c> means clicking one hands the URL to nobody;
/// and no script runs on these pages to read the DOM.
/// </para>
/// <para>
/// A blanket "not anywhere" assertion would now have to be deleted outright to make this slice
/// pass, which is exactly the kind of quiet weakening that loses a security property. Replacing it
/// with a precise one keeps the guarantee testable.
/// </para>
/// </remarks>
internal static partial class TokenExposure
{
    /// <summary>
    /// Asserts <paramref name="token"/> appears nowhere in <paramref name="html"/> except inside the
    /// <c>href</c> of a same-origin link under <c>/angebot/</c>.
    /// </summary>
    internal static void AssertOnlyInSameOriginLinks(string html, string token)
    {
        // Visible text: everything outside a tag. The customer must never be able to read, select
        // or copy the credential out of the page body.
        var visibleText = Tags().Replace(html, " ");
        Assert.DoesNotContain(token, visibleText, StringComparison.Ordinal);

        // Any input element — hidden or otherwise. The antiforgery field is the only hidden input a
        // customer page carries, and it must stay that way.
        foreach (Match input in Inputs().Matches(html))
        {
            Assert.DoesNotContain(token, input.Value, StringComparison.Ordinal);
        }

        // Never as a query-string parameter, on any attribute.
        Assert.DoesNotContain($"?token={token}", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"&token={token}", html, StringComparison.Ordinal);

        // Whatever is left must be an href to this site's own Angebot routes.
        foreach (Match occurrence in Attributes().Matches(html))
        {
            if (!occurrence.Value.Contains(token, StringComparison.Ordinal))
            {
                continue;
            }

            Assert.Equal("href", occurrence.Groups[1].Value.ToLowerInvariant());
            Assert.StartsWith("/angebot/", occurrence.Groups[2].Value, StringComparison.Ordinal);
        }
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex("<input[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Inputs();

    [GeneratedRegex("([A-Za-z-]+)=\"([^\"]*)\"")]
    private static partial Regex Attributes();
}
