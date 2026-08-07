using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Projects.Dtos;

/// <summary>
/// The Project detail read (SRS FR-7.4, Wireframe E1). A separate shape from
/// <see cref="ProjectDto"/> rather than an extension of it: conversion returns what it just
/// created, whereas this answers "show me this Project and where it came from", and the two
/// diverge further in Phase 8 when Invoices arrive.
///
/// <para>
/// <b>Fields come from E1, minus what does not exist yet.</b> E1 renders the customer's name, the
/// status, the agreed total, and an "Originating: Lead | Inspection | Angebot ANG-2026-00042" line
/// — all present here. **FR-7.4's Invoice portion (the invoice table, "Invoiced" and "Remaining")
/// is deliberately absent and deferred to Phase 8**, which is when Invoices come into existence;
/// it is a documented gap, not an oversight.
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
    string AngebotNumber);
