using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Infrastructure.Persistence.ValueConverters;

/// <summary>Round-trips via ItemUnit.Code / ItemUnit.FromCode — the single round-trip surface ItemUnit's own doc comment already anticipated for Infrastructure (Domain has zero EF Core awareness).</summary>
public sealed class ItemUnitConverter() : ValueConverter<ItemUnit, string>(
    unit => unit.Code,
    code => ItemUnit.FromCode(code));
