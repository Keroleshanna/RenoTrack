using System.Text.Json.Serialization;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Api.Angebote.Dtos;

// ---- expectedVersion (D115) ------------------------------------------------------------------
//
// Every write that can change an Angebot's content or move it through review carries the
// `version` the caller last read, so a write prepared against an older view of the document is a
// 409 rather than a silent overwrite. It is `[JsonRequired]`: a missing value is a 400 from model
// binding, never a quiet default of 0 that would happen to match a brand-new Angebot. On a DELETE
// it travels as a required query parameter instead, because a DELETE carries no body. Never an
// `If-Match` header — that convention answers 412, and the approved contract is 409.

/// <summary>
/// The whole body of a write that otherwise needs none — submit, approve and "return for
/// correction". The Angebot comes from the route and the actor from the token (D61).
/// </summary>
public sealed record ExpectedVersionRequest([property: JsonRequired] int ExpectedVersion);

/// <summary>
/// Creates a Draft Angebot for a Lead. <c>LeadId</c> comes from the route and the creating
/// Inspector from the token, never the body (D61).
/// </summary>
/// <param name="InspectionId">
/// Optional link to the Inspection this quote came out of. A genuine input rather than a
/// server-derived value: it names <em>what the work is based on</em>, not who is acting.
/// </param>
public sealed record CreateAngebotRequest(int? InspectionId);

/// <summary>Adds a section to an editable Angebot.</summary>
public sealed record AddSectionRequest(
    string Title,
    int SortOrder,
    [property: JsonRequired] int ExpectedVersion);

/// <summary>
/// The Admin's comment when returning an Angebot to the Inspector (SRS FR-5.2). The reviewing
/// Admin's id comes from the token, so the comment text is the only genuine input.
/// </summary>
public sealed record RequestChangesRequest(
    string Comment,
    [property: JsonRequired] int ExpectedVersion);

/// <summary>
/// Adds a line item, in either of SRS FR-4.9's two modes: pick a Catalog entry
/// (<paramref name="CatalogItemId"/> set, description/specification/unit pre-filled from it), or
/// type a fully custom line. The existing handler resolves which mode applies; the controller does
/// not branch on it.
/// </summary>
public sealed record AddItemRequest(
    int SectionId,
    int? CatalogItemId,
    string? Description,
    string? Specification,
    string? UnitCode,
    decimal Quantity,
    decimal UnitPrice,
    VatRate VatRate,
    [property: JsonRequired] int ExpectedVersion);

/// <summary>
/// Duplicates an entire Angebot onto another Lead (SRS FR-4.11). The source is the route id; the
/// acting Inspector comes from the token.
/// </summary>
/// <param name="TargetLeadId">
/// Which Lead the new Draft belongs to — who is being acted <em>upon</em>, so a genuine input
/// rather than a server-derived value (D61's own correction).
/// </param>
/// <summary>
/// The complete set of an existing line's editable values (`PermissionMatrix.md` §3, Phase 10).
/// Sent to <c>PUT /api/v1/angebote/{id}/items/{itemId}</c>, which replaces all of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Narrower than <see cref="AddItemRequest"/> by two fields, and both omissions are deliberate.</b>
/// <c>SectionId</c> is absent because the line already belongs to a section and the handler finds
/// it by asking which section holds the item — accepting one would let a caller name a different
/// section of the same Angebot and quietly move the line while claiming to edit it.
/// <c>CatalogItemId</c> is absent because editing has only one mode: FR-4.9's two modes exist to
/// decide where a *new* line's values come from, whereas here the caller supplies them outright.
/// Re-pointing a line at a different Catalog entry is a different line, and add+remove says so.
/// </para>
/// <para>
/// <c>Description</c> and <c>UnitCode</c> are therefore required, where the add request makes them
/// optional — there is no Catalog entry to fall back on.
/// </para>
/// </remarks>
public sealed record UpdateAngebotItemRequest(
    string Description,
    string? Specification,
    string UnitCode,
    decimal Quantity,
    decimal UnitPrice,
    VatRate VatRate,
    [property: JsonRequired] int ExpectedVersion);

public sealed record DuplicateAngebotRequest(int TargetLeadId);
