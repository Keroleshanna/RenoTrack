namespace RenoTrack.Application.Angebote.Commands.AddAngebotSection;

/// <summary>
/// Adds a section to an editable Angebot. Owning Inspector or any Admin (<c>PermissionMatrix.md</c>
/// §3, D115).
/// </summary>
/// <param name="ExpectedVersion">The <c>Version</c> the caller last read; a mismatch is a 409 (D115).</param>
/// <param name="RequestingInspectorId">
/// The scope: the Inspector's own id, or <c>null</c> for an Admin, who is unrestricted (D88). Never
/// from the body.
/// </param>
/// <param name="PerformedByUserId">The actor, Admin or Inspector, for the audit row (D88).</param>
public sealed record AddAngebotSectionCommand(
    int AngebotId,
    string Title,
    int SortOrder,
    int ExpectedVersion,
    int? RequestingInspectorId,
    int PerformedByUserId);
