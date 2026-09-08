using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RenoTrack.Website.Pages;

/// <summary>
/// What a customer sees when something on this site fails.
/// </summary>
/// <remarks>
/// <para>
/// <b>This page is customer-reachable, which is why it is not the scaffold's.</b>
/// <c>UseExceptionHandler("/Error")</c> re-executes into it, so an unhandled exception on
/// <c>/angebot/{token}</c> lands here — and the template version rendered the marketing layout with
/// jQuery and Bootstrap, English copy, links into a site that no longer exists, and a request
/// identifier. Every one of those contradicts a rule this project already holds: no script element
/// on a customer page, German only (Q8), and nothing internal reaching the customer (D100 Part 4).
/// </para>
/// <para>
/// <b>No diagnostic identifier is shown.</b> The scaffold displayed <c>Activity.Current?.Id ??
/// TraceIdentifier</c> so a user could quote it to support. This site's audience is a customer
/// holding a link, there is no support desk to quote it to, and the correlation belongs in the
/// server's own logs where it already is. Showing one would also put an internal value on a page
/// whose URL is a credential — the same instinct <c>RouteDiagnostics</c> exists to resist on the
/// API side.
/// </para>
/// <para>
/// The model carries no state at all, deliberately: there is nothing about the failure that a
/// customer can act on, and anything it held would be something to leak.
/// </para>
/// </remarks>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorModel : PageModel
{
    public void OnGet()
    {
    }
}
