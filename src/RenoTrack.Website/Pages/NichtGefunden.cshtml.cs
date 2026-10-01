using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RenoTrack.Website.Site;

namespace RenoTrack.Website.Pages;

/// <summary>
/// The marketing site's not-found page (<b>D103</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Always a 404.</b> Rendering it with a 200 would make every mistyped address an indexable page.
/// </para>
/// <para>
/// <b>A bare 404 when this endpoint is not a marketing page</b> — that is, when the marketing site is
/// disabled and the startup convention never gave it metadata. A token-only deployment therefore keeps
/// exactly the 404s it had, and this page never renders a layout whose services were not registered. The
/// decision is read from the endpoint, never from configuration.
/// </para>
/// </remarks>
public sealed class NichtGefundenModel : PageModel
{
    public IActionResult OnGet()
    {
        if (!HttpContext.IsMarketingPage())
        {
            return NotFound();
        }

        Response.StatusCode = StatusCodes.Status404NotFound;
        return Page();
    }
}
