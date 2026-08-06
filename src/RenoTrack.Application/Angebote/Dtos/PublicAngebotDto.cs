using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Angebote.Dtos;

/// <summary>
/// Whether the customer has answered this Angebot yet, and how.
///
/// A dedicated public type, never <see cref="AngebotStatus"/>: the public contract stays
/// independent of the internal workflow, so a future internal state cannot accidentally become
/// part of the API a customer's browser depends on. Three values, because three are all a customer
/// can ever meaningfully be told about their own decision.
/// </summary>
public enum PublicAngebotDecision
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>One priced line, as the customer sees it (Wireframe A3's item row).</summary>
public sealed record PublicItemDto(
    string Description,
    string? Specification,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal LineTotal);

/// <summary>One "Pos. N" block with its <c>Zwischensumme</c> (Wireframe A3).</summary>
public sealed record PublicSectionDto(
    string Title,
    decimal Subtotal,
    IReadOnlyList<PublicItemDto> Items);

/// <summary>
/// One <c>zzgl. N% MwSt</c> line. <see cref="Rate"/> is the percentage itself (0/7/16/19) via
/// <c>VatRate.ToPercentage()</c>, not an enum name: the customer-facing page renders
/// "zzgl. 19% MwSt", and publishing the internal member name ("Standard") would both read as
/// nonsense and put internal vocabulary into the public contract — the same reason
/// <see cref="PublicAngebotDecision"/> exists. <c>decimal</c> rather than <c>int</c> because that
/// is the Domain accessor's own type, so no conversion is invented on the way out.
/// </summary>
public sealed record PublicVatLineDto(decimal Rate, decimal VatAmount);

/// <summary>
/// The Angebot as an unauthenticated token-link holder may see it (SRS FR-6.2, Wireframe A3).
///
/// <para>
/// <b>Deliberately a separate hierarchy from <see cref="AngebotDetailDto"/>, not a projection of
/// it.</b> If the two shared a type, a field added later for the Dashboard would silently appear
/// on the one endpoint in this system that any anonymous caller holding a forwarded email can
/// reach. The duplication is the safety property, not an oversight.
/// </para>
/// <para>
/// <b>What is absent is absent on purpose</b> (design review, Phase 6 Slice 3): the internal
/// Angebot/section/item ids, <c>LeadId</c>, <c>InspectionId</c>, <c>CreatedByInspectorId</c> and
/// <c>ReviewedByAdminId</c> (staff identities — a forwarded link must not disclose which employee
/// priced the job or which manager approved it), <c>CatalogItemId</c> (BR-8 traceability; would
/// disclose that pricing comes from a reusable template catalogue, and which template),
/// <c>CreatedAt</c>/<c>SentAt</c>, <c>SortOrder</c>, per-item <c>VatRate</c>, per-rate net amounts,
/// and every Lead field. None has a documented customer-facing use, and the default on this
/// surface is to expose nothing without one.
/// </para>
/// </summary>
public sealed record PublicAngebotDto(
    string AngebotNumber,
    PublicAngebotDecision Decision,
    DateTime? DecisionAt,
    decimal NetTotal,
    IReadOnlyList<PublicVatLineDto> VatBreakdown,
    decimal GrossTotal,
    IReadOnlyList<PublicSectionDto> Sections);

public static class PublicAngebotMappingExtensions
{
    public static PublicAngebotDto ToPublicDto(this Angebot angebot) => new(
        angebot.AngebotNumber,
        ToPublicDecision(angebot.Status),
        angebot.DecisionAt,
        angebot.NetTotal.Amount,
        [.. angebot.VatBreakdown.Select(line => new PublicVatLineDto(line.Rate.ToPercentage(), line.VatAmount.Amount))],
        angebot.GrossTotal.Amount,

        // Ordered here rather than exposing SortOrder, so the customer's page cannot render the
        // document in an order the company never intended.
        [.. angebot.Sections
            .OrderBy(section => section.SortOrder)
            .ThenBy(section => section.Id)
            .Select(ToPublicDto)]);

    private static PublicSectionDto ToPublicDto(AngebotSection section) => new(
        section.Title,
        section.Subtotal.Amount,
        [.. section.Items.Select(item => new PublicItemDto(
            item.Description,
            item.Specification,
            item.Quantity,
            item.Unit.Code,
            item.UnitPrice.Amount,
            item.LineTotal.Amount))]);

    /// <summary>
    /// Every other internal state collapses to <see cref="PublicAngebotDecision.Pending"/> rather
    /// than throwing, because none of them is reachable through a token link: a link only exists
    /// once the Angebot has been sent. Defaulting keeps the public surface incapable of leaking a
    /// state it should never see, even if a future path made one reachable.
    /// </summary>
    private static PublicAngebotDecision ToPublicDecision(AngebotStatus status) => status switch
    {
        AngebotStatus.CustomerApproved => PublicAngebotDecision.Approved,
        AngebotStatus.CustomerRejected => PublicAngebotDecision.Rejected,
        _ => PublicAngebotDecision.Pending,
    };
}
