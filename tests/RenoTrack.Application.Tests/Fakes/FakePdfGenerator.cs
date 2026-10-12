using RenoTrack.Application.Common.Documents;
using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// Hand-written fake, no mocking framework (CLAUDE.md §14). The real renderer is
/// <c>MigraDocPdfGenerator</c>, proved in <c>RenoTrack.Documents.Tests</c>; what these tests need is
/// to see which document a handler asked for, and to tell one rendering from another.
/// </summary>
public sealed class FakePdfGenerator : IPdfGenerator
{
    public List<InvoiceDocument> RenderedInvoices { get; } = [];

    /// <summary>Set to make every render throw, standing in for a configuration or renderer fault.</summary>
    public Exception? RenderFailure { get; set; }

    /// <summary>
    /// A fresh array per call whose content names the call, so a test asserting "these are the bytes
    /// that were rendered" cannot be satisfied by a second rendering — a real PDF differs on every
    /// render too, because it embeds its creation time.
    /// </summary>
    public byte[] RenderInvoice(InvoiceDocument document)
    {
        if (RenderFailure is not null)
        {
            throw RenderFailure;
        }

        RenderedInvoices.Add(document);
        return System.Text.Encoding.ASCII.GetBytes($"%PDF-fake invoice {document.InvoiceNumber} #{RenderedInvoices.Count}");
    }

    public byte[] RenderAngebot(AngebotDocument document) =>
        throw new NotSupportedException("No Application handler renders an Angebot.");
}
