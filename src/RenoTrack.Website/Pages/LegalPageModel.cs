using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RenoTrack.Website.Content;

namespace RenoTrack.Website.Pages;

/// <summary>
/// What the Impressum and the Datenschutzerklärung have in common: a configured document, or a 404.
/// </summary>
/// <remarks>
/// <para>
/// <b>The base class exists for the 404 rule, not to save four lines.</b> "No configured content
/// means the route does not exist" (<b>D100</b> Part 1) is the security- and honesty-relevant half
/// of these pages, and duplicating it across two page models is how one of them eventually drifts
/// into serving an empty legal page.
/// </para>
/// <para>
/// <b>Thin in the sense <c>CLAUDE.md</c> §22 requires.</b> It validates nothing, decides no business
/// rule, and reads no aggregate — a legal page has no Domain behind it at all, which is why this
/// slice touches no layer below the Website.
/// </para>
/// </remarks>
public abstract class LegalPageModel : PageModel
{
    /// <summary>The document this page renders.</summary>
    public abstract LegalDocumentOptions Document { get; }

    /// <summary>The page's own German name, used for both the title and the heading.</summary>
    /// <remarks>
    /// Fixed in code rather than configured, and that is not a breach of Q7's "invent nothing":
    /// these are the names <c>SRS</c> FR-1.4 gives the two required pages, not content about the
    /// company. Everything the *company* says arrives through configuration.
    /// </remarks>
    public abstract string PageName { get; }

    public IActionResult OnGet() => Document.HasContent ? Page() : NotFound();
}
