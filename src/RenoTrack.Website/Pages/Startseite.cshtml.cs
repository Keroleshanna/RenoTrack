using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RenoTrack.Website.Site;

namespace RenoTrack.Website.Pages;

/// <summary>
/// The marketing site's homepage at <c>/</c> (<b>D104</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Named <c>Startseite</c>, never <c>Index</c>.</b> An <c>Index</c> page would also answer <c>/Index</c>;
/// the explicit <c>@page "/"</c> template on this page replaces the page-name route, so <c>/startseite</c>
/// does not exist either and the homepage has exactly one address.
/// </para>
/// <para>
/// <b>A bare 404 when this endpoint is not a marketing page</b> — that is, when the marketing site is
/// disabled and the startup convention never gave it metadata. A token-only deployment therefore keeps the
/// bare 404 at <c>/</c> it has had since the scaffold was removed, and this page never renders a layout whose
/// services were not registered. The decision is read from the endpoint, never from configuration.
/// </para>
/// </remarks>
public sealed class StartseiteModel : PageModel
{
    public IActionResult OnGet() => HttpContext.IsMarketingPage() ? Page() : NotFound();
}
