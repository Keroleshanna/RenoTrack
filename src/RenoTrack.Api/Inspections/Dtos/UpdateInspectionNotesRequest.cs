namespace RenoTrack.Api.Inspections.Dtos;

/// <summary>
/// The Inspector's request to record or revise an Inspection's notes (SRS FR-3.3, Sequence
/// Diagram §3 Step B, PermissionMatrix.md §2 "Edit Inspection notes").
/// </summary>
/// <param name="Notes">
/// The free-text observations. <b>Nullable on purpose:</b> sending <c>null</c> is the documented way
/// to clear notes — <c>Inspection.UpdateNotes</c> accepts null and its validator deliberately places
/// no rule on this field. No length cap is imposed, because no project document states one; the
/// effective bound is Kestrel's ~30 MB default body limit, the same position Slice 5 took for
/// <c>Lead.Notes</c> rather than inventing a number.
/// </param>
/// <remarks>
/// Unlike <see cref="ScheduleInspectionRequest"/> this record carries no user id at all, and unlike
/// completion it is not empty. D61's subset rule produces all three shapes from one principle: the
/// Inspection id comes from the route, the acting Inspector's id from the JWT's <c>sub</c> claim
/// (this is the "who is acting" case), and <c>Notes</c> is the only value a caller may legitimately
/// supply — which is exactly what justifies a request record existing here.
/// </remarks>
public sealed record UpdateInspectionNotesRequest(string? Notes);
