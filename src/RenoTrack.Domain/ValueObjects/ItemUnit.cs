namespace RenoTrack.Domain.ValueObjects;

/// <summary>
/// The unit of measure for an AngebotItem/CatalogItem quantity (SRS.md FR-4.3). A value
/// object rather than a bare enum + string pair: an enum-plus-string representation can
/// express contradictory states (e.g. Kind=SquareMeter with a non-null CustomLabel, or
/// Kind=Custom with no label at all) that nothing forces callers to prevent. Making the
/// constructor private and only exposing validating factory methods makes those
/// contradictions impossible to construct in the first place, instead of relying on
/// validation scattered across every call site.
/// </summary>
public sealed record ItemUnit
{
    /// <summary>
    /// The canonical wire/storage codes for the five standard units, exactly as written in
    /// SRS.md FR-4.3. This is also the single source of truth Infrastructure's EF Core
    /// value converter (Phase 3) reads/writes through <see cref="Code"/> and
    /// <see cref="FromCode"/> — Domain owns the mapping, Infrastructure just persists it,
    /// so RenoTrack.Domain never needs to reference EF Core (Architecture.md §3).
    /// </summary>
    private static readonly IReadOnlyDictionary<UnitKind, string> StandardCodes =
        new Dictionary<UnitKind, string>
        {
            [UnitKind.SquareMeter] = "m2",
            [UnitKind.Piece] = "Stk",
            [UnitKind.LinearMeter] = "lfm",
            [UnitKind.LumpSum] = "pauschal",
            [UnitKind.Meter] = "m"
        };

    public UnitKind Kind { get; }

    /// <summary>Set only when <see cref="Kind"/> is <see cref="UnitKind.Custom"/>; null otherwise.</summary>
    public string? CustomLabel { get; }

    private ItemUnit(UnitKind kind, string? customLabel)
    {
        Kind = kind;
        CustomLabel = customLabel;
    }

    public static ItemUnit SquareMeter() => new(UnitKind.SquareMeter, null);
    public static ItemUnit Piece() => new(UnitKind.Piece, null);
    public static ItemUnit LinearMeter() => new(UnitKind.LinearMeter, null);
    public static ItemUnit LumpSum() => new(UnitKind.LumpSum, null);
    public static ItemUnit Meter() => new(UnitKind.Meter, null);

    /// <summary>
    /// Creates a unit for anything outside the five standard ones SRS.md FR-4.3 names
    /// explicitly ("m², Stk, lfm, pauschal, m, etc.") — the SRS's own "etc." means this list
    /// is intentionally open, unlike the closed LeadStatus/AngebotStatus state machines.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="label"/> is empty, or (case-insensitively) collides with a
    /// reserved standard unit code. The collision is rejected here, at construction time,
    /// rather than resolved by guesswork when later reading a stored value back: if "m2"
    /// were allowed as a custom label, a value round-tripped from storage could not tell
    /// apart a custom unit that happens to be named "m2" from the real, standard square-meter
    /// unit. Refusing the ambiguous input up front keeps every stored value unambiguous by
    /// construction, and fails loudly and immediately for whoever typed it, instead of
    /// silently reinterpreting their custom unit as a standard one later.
    /// </exception>
    public static ItemUnit Custom(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Custom unit label is required.", nameof(label));

        var trimmed = label.Trim();
        if (StandardCodes.Values.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"'{trimmed}' is a reserved standard unit code and cannot be used as a custom unit label.",
                nameof(label));
        }

        return new ItemUnit(UnitKind.Custom, trimmed);
    }

    /// <summary>The canonical storage/display code: the standard SRS code, or the custom label.</summary>
    public string Code => Kind == UnitKind.Custom ? CustomLabel! : StandardCodes[Kind];

    /// <summary>Reconstructs an <see cref="ItemUnit"/> from a value previously produced by <see cref="Code"/>.</summary>
    public static ItemUnit FromCode(string code)
    {
        var match = StandardCodes.FirstOrDefault(kv =>
            string.Equals(kv.Value, code, StringComparison.OrdinalIgnoreCase));

        return match.Value is not null
            ? new ItemUnit(match.Key, null)
            : Custom(code);
    }

    public override string ToString() => Code;
}
