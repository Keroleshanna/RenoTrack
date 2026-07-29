using RenoTrack.Domain.Enums;

namespace RenoTrack.Domain.ValueObjects;

/// <summary>
/// One row of the Angebot's "zzgl. X% MwSt" summary (Wireframes.md A3) — the net amount and
/// VAT amount for a single VAT rate actually present among the Angebot's items. Always
/// computed fresh from the live item collection (<see cref="Entities.Angebot.VatBreakdown"/>);
/// see that property for why this differs from NetTotal/GrossTotal's storage strategy.
/// </summary>
public sealed record VatBreakdownLine(VatRate Rate, Money NetAmount, Money VatAmount);
