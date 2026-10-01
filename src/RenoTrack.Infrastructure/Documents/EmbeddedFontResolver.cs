using System.Reflection;
using PdfSharp.Fonts;

namespace RenoTrack.Infrastructure.Documents;

/// <summary>
/// Supplies the one typeface every generated document is set in, from fonts embedded in this
/// assembly (Phase 14, <b>D110</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>PDFsharp cannot resolve a single font on its own</b> — not even the one it falls back to when
/// something else fails. Without a resolver it throws <c>"The font 'Courier New' cannot be resolved
/// for predefined error font"</c>, on Windows as readily as on Linux. That was measured, not
/// assumed, which is also how the choice of library stopped being a paperwork decision: a generator
/// that renders only where the host happens to have fonts installed would pass on a developer's
/// machine and fail in the Linux CI job.
/// </para>
/// <para>
/// <b>Embedded, not read from disk.</b> A font file beside the application is one more thing a
/// deployment can forget, and its absence would surface as a failed invoice rather than a failed
/// startup. Embedding makes the document's appearance a property of the build.
/// </para>
/// <para>
/// <b>Every request is answered, whatever family is asked for.</b> MigraDoc asks for its own
/// fallback families by name, and a resolver returning <c>null</c> re-raises the error above. There
/// is one family here by design: a generated invoice is a legal document, not a place for
/// typographic variety, and the Website's brand font ships as WOFF2, which PDFsharp cannot read.
/// </para>
/// <para>
/// <b>Italic is mapped to the upright face deliberately.</b> Liberation Sans ships an italic, but no
/// template here uses one; mapping it rather than embedding a third file keeps the assembly smaller
/// and the output predictable. Should a template ever need italics, the file is added here and this
/// paragraph is what explains why it was not already.
/// </para>
/// <para>
/// <b>Liberation Sans</b>, SIL Open Font License 1.1 — the same licence as the Website's Figtree,
/// with the text committed beside the fonts in <c>Fonts/OFL.txt</c>. Metric-compatible with Arial,
/// which is what German business correspondence is usually set in.
/// </para>
/// </remarks>
public sealed class EmbeddedFontResolver : IFontResolver
{
    /// <summary>The family name every template asks for.</summary>
    public const string FontFamily = "Liberation Sans";

    internal const string RegularFaceName = "LiberationSans#Regular";
    internal const string BoldFaceName = "LiberationSans#Bold";

    private const string ResourcePrefix = "RenoTrack.Infrastructure.Documents.Fonts.";

    /// <summary>One instance is enough: it holds no state and PDFsharp sets it globally.</summary>
    public static EmbeddedFontResolver Instance { get; } = new();

    public byte[] GetFont(string faceName)
    {
        var file = faceName == BoldFaceName ? "LiberationSans-Bold.ttf" : "LiberationSans-Regular.ttf";

        using var stream = typeof(EmbeddedFontResolver).Assembly
            .GetManifestResourceStream(ResourcePrefix + file)
            ?? throw new InvalidOperationException(
                $"The embedded font '{file}' is missing from {typeof(EmbeddedFontResolver).Assembly.GetName().Name}. " +
                "It is an EmbeddedResource in the project file; a build that drops it produces documents that cannot render.");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Answers for every family, because MigraDoc asks for families this product never names —
    /// its own error font among them.
    /// </summary>
    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? BoldFaceName : RegularFaceName);

    /// <summary>
    /// Installs this resolver globally, once per process. PDFsharp keeps the resolver in a static
    /// field and refuses a second one after a font has been resolved, so this is idempotent rather
    /// than a setter anyone may call freely.
    /// </summary>
    public static void EnsureInstalled()
    {
        if (GlobalFontSettings.FontResolver is null)
        {
            GlobalFontSettings.FontResolver = Instance;
        }
    }
}
