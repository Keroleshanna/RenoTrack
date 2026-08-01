using FluentValidation;

namespace RenoTrack.Application.Inspections.Commands.UploadInspectionPhoto;

public sealed class UploadInspectionPhotoCommandValidator : AbstractValidator<UploadInspectionPhotoCommand>
{
    /// <summary>
    /// Maximum length of the file extension carried over from the caller's filename, including the
    /// leading dot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An application-level defensive bound against pathological input — deliberately <b>not</b>
    /// derived from any platform path limit such as Windows' historical <c>MAX_PATH</c>, since
    /// long-path support makes that a moving target and a rule pinned to it would be unstable.
    /// 32 is generous against reality: the longest extensions this system will plausibly see
    /// (<c>.jpeg</c>, <c>.tiff</c>, <c>.webp</c>, <c>.heic</c>, <c>.avif</c>) are five characters.
    /// </para>
    /// <para>
    /// The problem it solves is concrete and was measured, not imagined: the extension is copied
    /// verbatim into the storage key, so a 300-character one composed a path long enough to fail at
    /// <c>File.Create</c> with an <c>IOException</c> — which the ProblemDetails middleware does not
    /// map, turning an ordinary bad request into a 500.
    /// </para>
    /// </remarks>
    public const int MaxFileExtensionLength = 32;

    public UploadInspectionPhotoCommandValidator()
    {
        RuleFor(c => c.InspectionId).GreaterThan(0);
        RuleFor(c => c.UploadedByInspectorId).GreaterThan(0);
        RuleFor(c => c.FileName).NotEmpty();
        RuleFor(c => c.FileContent).NotNull();

        RuleFor(c => c.FileName)
            .Must(HasUsableExtension)
            .When(c => !string.IsNullOrEmpty(c.FileName))
            .WithMessage(
                $"The file extension must be a dot followed by up to {MaxFileExtensionLength - 1} letters or digits, or absent entirely.");
    }

    /// <summary>
    /// Whether the extension implied by <paramref name="fileName"/> can safely become part of a
    /// stored filename.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An allowlist of characters, deliberately — not of file types.</b> Nothing here restricts
    /// which image formats may be uploaded: <c>.heic</c>, <c>.avif</c>, <c>.dng</c>, <c>.cr2</c> and
    /// anything else alphanumeric all pass. No document specifies permitted formats, and inventing
    /// that rule would be a product decision rather than a filesystem guard.
    /// </para>
    /// <para>
    /// <b>Why not <c>Path.GetInvalidFileNameChars()</c>.</b> Measured directly: it returns 41
    /// characters on Windows (including <c>" &lt; &gt; | : * ? \ /</c>) but only two on Linux
    /// (<c>NUL</c> and <c>/</c>). Validation built on it would accept or reject the same request
    /// differently depending on the deployment's operating system — and, concretely, a test
    /// asserting that <c>.jp*g</c> is rejected would pass on the Windows test job and fail on the
    /// Linux one. A positive character class behaves identically everywhere and fails closed for
    /// characters nobody anticipated.
    /// </para>
    /// <para>
    /// A filename with no extension stays valid; the resulting storage key simply has none. Only a
    /// <em>present but unusable</em> extension is rejected.
    /// </para>
    /// </remarks>
    private static bool HasUsableExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);

        if (extension.Length == 0)
        {
            return true;
        }

        return extension.Length <= MaxFileExtensionLength
            && extension[0] == '.'
            && extension.Length > 1
            && extension.Skip(1).All(char.IsAsciiLetterOrDigit);
    }
}
