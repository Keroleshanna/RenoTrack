using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RenoTrack.Website.Content;
using RenoTrack.Website.Site;

namespace RenoTrack.Website.Pages;

/// <summary>
/// One service's page at <c>/leistungen/{slug}</c> (<b>D105</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The slug is matched exactly</b> — ordinal and case-sensitive, through <see cref="MarketingSite.FindService"/>.
/// An unknown slug is a bodyless 404, which the site's not-found page renders without echoing the address. There
/// is no normalisation, transliteration, closest match or fallback to another service. An upper-case or
/// trailing-slash address never reaches this lookup: the canonical-path redirect answers it first.
/// </para>
/// <para>
/// <b>A bare 404 when this endpoint is not a marketing page</b>, as for every marketing page. Only then is the
/// startup snapshot resolved: it is registered only when the site is enabled, so it cannot be a constructor
/// dependency of a page that also exists on a token-only deployment.
/// </para>
/// </remarks>
public sealed class LeistungModel : PageModel
{
    /// <summary>The service this page presents; set whenever the page renders.</summary>
    public ServiceOptions Service { get; private set; } = null!;

    public IActionResult OnGet(string? slug)
    {
        if (!HttpContext.IsMarketingPage())
        {
            return NotFound();
        }

        var service = HttpContext.RequestServices.GetRequiredService<MarketingSite>().FindService(slug);
        if (service is null)
        {
            return NotFound();
        }

        Service = service;
        return Page();
    }
}
