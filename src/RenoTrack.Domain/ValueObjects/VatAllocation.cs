using RenoTrack.Domain.Enums;

namespace RenoTrack.Domain.ValueObjects;

/// <summary>
/// The Net/VAT/Gross totals of a document whose gross figure was chosen first and split afterwards
/// — the shape SRS FR-8.2 requires of an Invoice ("an amount (net + VAT breakdown consistent with
/// the originating Angebot's rates)") and Sequence Diagram §8 describes ("Derive Net/VAT split
/// proportionally from the Angebot's VAT-rate mix").
///
/// <para>
/// This lives in the Domain because it is BR-11 money arithmetic, and Architecture.md §6.1 says the
/// Angebot's own calculation "is reused for Invoices". It is a pure function over a rate mix and a
/// target: it knows nothing about Angebote, Invoices or Projects, and reaches no repository.
/// </para>
/// <para>
/// <b>The per-rate detail is returned as <see cref="Lines"/>, one per rate, ordered by rate</b>
/// (Phase 14 Slice 2, D111). Phase 8 computed the split and discarded it, because nothing stored
/// or read it; an Invoice document has to print "applicable VAT rate(s) and VAT amount(s)" (BR-5),
/// so <c>Invoice</c> now keeps exactly these lines. The totals are the lines' sums, never a second
/// calculation, so the two can never disagree.
/// </para>
/// </summary>
public sealed record VatAllocation(
    IReadOnlyList<VatBreakdownLine> Lines,
    Money NetAmount,
    Money VatAmount,
    Money GrossAmount)
{
    /// <summary>
    /// Value equality over the lines too. A record compares a list member by reference, which would
    /// make two identical allocations unequal the moment <see cref="Lines"/> was added; this keeps
    /// "the same input always produces the same result" a statement about values.
    /// </summary>
    public bool Equals(VatAllocation? other) =>
        other is not null
        && NetAmount == other.NetAmount
        && VatAmount == other.VatAmount
        && GrossAmount == other.GrossAmount
        && Lines.SequenceEqual(other.Lines);

    public override int GetHashCode() => HashCode.Combine(NetAmount, VatAmount, GrossAmount, Lines.Count);

    /// <summary>
    /// Splits <paramref name="targetGross"/> across the VAT rates present in
    /// <paramref name="rateMix"/>, in proportion to each rate's share of that mix's own gross.
    ///
    /// <para>
    /// <b>The result always satisfies <c>NetAmount + VatAmount == GrossAmount</c> exactly</b>, which
    /// is what <c>Invoice.Create</c> re-checks structurally. Two mechanisms make that true rather
    /// than approximately true: rounded per-rate shares are reconciled against the target before
    /// anything else happens, and within each rate the VAT is derived as <c>share − net</c> rather
    /// than recomputed from the rate, so a rounded net cannot leave a stray cent behind.
    /// </para>
    /// <para>
    /// <b>A zero target allocates to zero without dividing</b>, and produces no lines: there is
    /// nothing to split, so no rate has a share. That path is taken before the mix's gross is ever
    /// used as a divisor, so a zero-valued Angebot and a zero-valued Invoice compose safely.
    /// </para>
    /// <para>
    /// The residual-cent rule is deterministic rounding machinery, not policy: the mix is ordered by
    /// rate and any residual lands on the largest-gross rate group (ties going to the higher rate).
    /// Since Slice 2 the lines are stored, so <i>which</i> group receives it is visible on the
    /// document — which is exactly why it must be a fixed function of the input, pinned by tests,
    /// rather than an accident of ordering.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException">The rate mix or the target is null.</exception>
    /// <exception cref="ArgumentException">
    /// The target is negative, or a positive target was given a rate mix whose own gross is zero —
    /// there is no proportion to allocate by. Callers are expected to reject that case with their
    /// own domain-appropriate error before reaching here; this is the backstop.
    /// </exception>
    public static VatAllocation ProportionalTo(IReadOnlyList<VatBreakdownLine> rateMix, Money targetGross)
    {
        ArgumentNullException.ThrowIfNull(rateMix);
        ArgumentNullException.ThrowIfNull(targetGross);

        if (targetGross.Amount < 0)
            throw new ArgumentException("Target gross cannot be negative.", nameof(targetGross));

        // Before any division: nothing to split, so nothing to divide by.
        if (targetGross == Money.Zero)
            return new VatAllocation([], Money.Zero, Money.Zero, Money.Zero);

        // Ordered so the whole calculation — including which group absorbs the residual — is a
        // function of the input alone, never of the order a caller happened to build the list in.
        var lines = rateMix.OrderBy(line => line.Rate).ToArray();
        var groupGross = lines.Select(line => line.NetAmount + line.VatAmount).ToArray();
        var mixGross = Money.Sum(groupGross);

        if (mixGross == Money.Zero)
        {
            throw new ArgumentException(
                "Cannot allocate a positive gross across a rate mix whose own gross is zero — there is no proportion to allocate by.",
                nameof(rateMix));
        }

        var shares = groupGross
            .Select(gross => Money.RoundedPerBR11(targetGross.Amount * gross.Amount / mixGross.Amount))
            .ToArray();

        // Rounding each share independently can leave the sum a cent or two off the target. Placing
        // the difference on one group keeps the total exact; it is the only step that is a choice,
        // and it is bounded by the number of distinct rates.
        var residual = targetGross - Money.Sum(shares);
        if (residual != Money.Zero)
        {
            shares[IndexOfLargestGross(groupGross)] += residual;
        }

        var allocated = new VatBreakdownLine[lines.Length];

        for (var i = 0; i < lines.Length; i++)
        {
            var multiplier = 1m + (lines[i].Rate.ToPercentage() / 100m);
            var net = Money.RoundedPerBR11(shares[i].Amount / multiplier);

            // Subtraction, not a second rate calculation: this is what guarantees
            // net + vat == share for every group, and therefore for the totals.
            allocated[i] = new VatBreakdownLine(lines[i].Rate, net, shares[i] - net);
        }

        return new VatAllocation(
            allocated,
            Money.Sum(allocated.Select(line => line.NetAmount)),
            Money.Sum(allocated.Select(line => line.VatAmount)),
            targetGross);
    }

    /// <summary>
    /// The largest group by gross. <paramref name="groupGross"/> is already ordered by ascending
    /// rate, and the comparison is <c>&gt;=</c>, so a tie resolves to the higher rate — one fixed
    /// answer for one input, which is all the reconciliation needs.
    /// </summary>
    private static int IndexOfLargestGross(Money[] groupGross)
    {
        var index = 0;
        for (var i = 1; i < groupGross.Length; i++)
        {
            if (groupGross[i].Amount >= groupGross[index].Amount)
                index = i;
        }

        return index;
    }
}
