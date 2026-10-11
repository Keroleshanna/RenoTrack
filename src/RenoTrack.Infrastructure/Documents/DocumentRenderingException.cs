namespace RenoTrack.Infrastructure.Documents;

/// <summary>
/// The renderer failed while composing or writing a document (Phase 14 Slice 3a, <b>D113</b>).
/// <see cref="MigraDocPdfGenerator"/> wraps every fault raised during rendering in this type.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why wrap at all:</b> MigraDoc, PDFsharp and the BCL are free to throw
/// <see cref="InvalidOperationException"/> or <see cref="ArgumentException"/> from deep inside a
/// render, and the API maps those to 409 and 400 because, everywhere else in this codebase, they come
/// from Domain guards (D59). Unwrapped, a renderer bug would reach an Admin as "conflict" or "bad
/// request". Wrapped, it is what it is — a server fault: an unmapped 500 with no detail in the body.
/// </para>
/// <para>
/// The original exception is kept as <see cref="Exception.InnerException"/>, so the stack trace the
/// <c>Error</c> log records still points at the real cause.
/// </para>
/// </remarks>
public sealed class DocumentRenderingException : Exception
{
    public DocumentRenderingException(Exception innerException)
        : base("The document could not be rendered.", innerException)
    {
    }
}
