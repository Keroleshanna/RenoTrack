using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Leads.Commands.CreateLead;

/// <summary>
/// Covers both creation paths in Sequence Diagram.md — §1 (public website form,
/// <paramref name="CreatedByUserId"/> null = system/anonymous) and §2 (Admin manual entry,
/// <paramref name="CreatedByUserId"/> set to the authenticated Admin) — the same distinction
/// Lead.cs's own <c>Create</c> factory already makes for <c>Address</c>/<c>Notes</c>.
/// </summary>
public sealed record CreateLeadCommand(
    string Name,
    string Phone,
    string Email,
    LeadSource Source,
    string? Address,
    string? Notes,
    int? CreatedByUserId);
