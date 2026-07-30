using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Infrastructure.Persistence.ValueConverters;

/// <summary>Round-trips via Money.Amount / Money.FromExact — the same exact-to-2-decimal-places invariant Money already enforces at construction.</summary>
public sealed class MoneyConverter() : ValueConverter<Money, decimal>(
    money => money.Amount,
    amount => Money.FromExact(amount));
