using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Projects.Dtos;

/// <summary>
/// The Project detail read (SRS FR-7.4, Wireframe E1). A separate shape from
/// <see cref="ProjectDto"/> rather than an extension of it: conversion returns what it just
/// created, whereas this answers "show me this Project and where it came from", and the two
/// diverge further in Phase 8 when Invoices arrive.
///
/// <para>
/// <b>Fields come from E1.</b> E1 renders the customer's name, the status, the agreed total, an
/// "Originating: Lead | Inspection | Angebot ANG-2026-00042" line, the
/// "Agreed Total / Invoiced / Remaining" header, and a table of the Project's Invoices — all
/// present here. **FR-7.4's Invoice portion arrived in Phase 8 Slice 6**; it was deferred through
/// Phase 7 only because Invoices did not exist yet.
/// </para>
/// <para>
/// <b><see cref="AlreadyInvoiced"/> and <see cref="Remaining"/> follow Slice 3's rules exactly, and
/// are computed rather than stored.</b> Every Invoice except <c>Void</c> counts (StateMachine.md
/// §3.3 excludes that one and no other, so a <c>Draft</c> counts exactly as a <c>Paid</c> does),
/// the figure summed is <c>GrossAmount</c>, and <c>Remaining = AgreedTotal − AlreadyInvoiced</c>
/// with **no clamping**: a negative remainder is BR-3's warning and must stay visible. No ERD
/// column exists for either, and none was added. They deliberately duplicate
/// <c>GET /api/v1/projects/{id}/invoice-balance</c>, because FR-7.4 requires the detail page to
/// show them "in one place" — a test asserts the two endpoints agree, so the duplication cannot
/// drift.
/// </para>
/// <para>
/// <b><see cref="Invoices"/> includes <c>Void</c> rows.</b> They are excluded from the two figures
/// above and from nothing else: BR-9 keeps a voided Invoice as a numbered, visible record rather
/// than hiding it, and a list that silently dropped one would make a gap in the numbering look
/// like a deleted document. Ordered <c>IssueDate</c> then <c>Id</c> — a list read must order
/// deterministically, and <c>IssueDate</c> alone is not unique.
/// </para>
/// <para>
/// <b>The whole of this response is Admin <c>F</c> / Inspector <c>R</c>, unscoped</b>
/// (`PermissionMatrix.md` §5), including the invoice list — it is Project-detail data, and it
/// confers no Invoice-management permission whatsoever. No <c>IOwnershipValidator</c> applies.
/// </para>
/// <para>
/// <b>Originating ids, not nested objects.</b> Consistent with CLAUDE.md §7's grow-on-demand rule
/// and with how the aggregates themselves relate — by id. A caller that needs the whole Lead or
/// Angebot has endpoints for both. <c>CustomerName</c> and <c>AngebotNumber</c> are the two
/// exceptions, because E1 renders them as text and a client would otherwise have to make two extra
/// round trips to draw one header line.
/// </para>
/// <para>
/// <b>No <c>InspectorId</c>/<c>AdminId</c> and no Customer contact details.</b> Nothing in E1
/// displays them, so they are not exposed — the same restraint the Phase 6 public DTO applies.
/// </para>
/// </summary>
public sealed record ProjectDetailDto(
    int Id,
    ProjectStatus Status,
    decimal AgreedTotal,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    int CustomerId,
    string CustomerName,
    int LeadId,
    int? InspectionId,
    int AngebotId,
    string AngebotNumber,
    decimal AlreadyInvoiced,
    decimal Remaining,
    IReadOnlyList<ProjectInvoiceDto> Invoices);
