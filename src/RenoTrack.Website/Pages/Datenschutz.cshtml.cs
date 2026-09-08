using RenoTrack.Website.Content;

namespace RenoTrack.Website.Pages;

/// <summary>
/// The Datenschutzerklärung required of a German telemedia offering (SRS FR-1.4).
/// </summary>
/// <remarks>
/// <b>No privacy text is compiled in</b>, for the same reason and under the same rule as the
/// Impressum: it is the company's statement about its own processing, supplied under
/// <c>Legal:Datenschutz</c>, and 404 until it exists (<b>D100</b> Part 1). Writing a plausible
/// privacy policy would be worse than having none, because a customer would believe it.
/// </remarks>
public sealed class DatenschutzModel(LegalContentOptions legal) : LegalPageModel
{
    public override LegalDocumentOptions Document => legal.Datenschutz;

    public override string PageName => "Datenschutzerklärung";
}
