using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RenoTrack.Website.Site;

namespace RenoTrack.Website.Pages;

/// <summary>
/// The services overview at <c>/leistungen</c> (<b>D105</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Never an <c>Index</c> page</b>, for the reason <c>Startseite</c> gives: the explicit <c>@page "/leistungen"</c>
/// template replaces the page-name route, so the overview has exactly one address.
/// </para>
/// <para>
/// <b>A bare 404 when this endpoint is not a marketing page</b> — the site is disabled and the startup convention
/// never gave it metadata. The decision is read from the endpoint, never from configuration.
/// </para>
/// </remarks>
public sealed class LeistungenModel : PageModel
{
    public IActionResult OnGet() => HttpContext.IsMarketingPage() ? Page() : NotFound();
}
