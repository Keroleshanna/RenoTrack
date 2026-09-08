using RenoTrack.Website.Content;

namespace RenoTrack.Website.Pages;

/// <summary>
/// The Impressum required of a German telemedia offering (SRS FR-1.4).
/// </summary>
/// <remarks>
/// <b>No legal text is compiled in.</b> The wording, the company's name, its address and its contact
/// details are all supplied per deployment under <c>Legal:Impressum</c> and authored by the company
/// (SRS §5, Phase 11 Q7). Until then this route answers 404 — see <b>D100</b> Part 1 for why an
/// empty page was refused as the alternative.
/// </remarks>
public sealed class ImpressumModel(LegalContentOptions legal) : LegalPageModel
{
    public override LegalDocumentOptions Document => legal.Impressum;

    public override string PageName => "Impressum";
}
