using RenoTrack.Application.Common.Documents;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Renders the company's customer-facing documents as PDF (Phase 14, <b>D110</b>;
/// <c>Architecture.md</c> §4).
/// </summary>
/// <remarks>
/// <para>
/// <b>One method per document type, named</b> — the same shape as <c>IEmailSender</c>, and for the
/// same reason: a generic <c>Render(template, data)</c> would hide which documents this system can
/// actually produce behind a string.
/// </para>
/// <para>
/// <b>It returns bytes and stores nothing.</b> Where a document is kept, and whether it is kept at
/// all, is the caller's decision — and for an invoice it is a legal one (an issued invoice is
/// archived as sent, never re-derived from data that may since have changed). Mixing generation and
/// storage would bury that decision inside a renderer.
/// </para>
/// <para>
/// <b>It grows one method per real use case</b>, exactly as the repositories do (§4). The Angebot
/// comes first because every figure it prints already exists; the invoice follows once the data
/// §14 UStG requires exists to print.
/// </para>
/// </remarks>
public interface IPdfGenerator
{
    /// <summary>Renders an Angebot. The returned bytes are a complete PDF file.</summary>
    byte[] RenderAngebot(AngebotDocument document);
}
