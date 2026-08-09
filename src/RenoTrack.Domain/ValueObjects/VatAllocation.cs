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
/// <b>The per-rate detail is deliberately not returned.</b> Only the three totals are, because only
/// those three exist as columns (ERD.md's <c>Invoices</c>) — <c>InvoiceLine</c> is deferred, so
/// there is nowhere to put a per-rate breakdown and nothing that reads one. The split is performed
/// per rate, which is what BR-6 requires; it is the *result* that is aggregated.
/// </para>
/// </summary>
public sealed record VatAllocation(Money NetAmount, Money VatAmount, Money GrossAmount)
{
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
    /// <b>A zero target allocates to zero without dividing.</b> That path is taken before the mix's
    /// gross is ever used as a divisor, so a zero-valued Angebot and a zero-valued Invoice compose
    /// safely.
    /// </para>
    /// <para>
    /// The residual-cent rule is deterministic rounding machinery, not policy: the mix is ordered by
    /// rate and any residual lands on the largest-gross rate group (ties going to the higher rate).
    /// Nothing outside this method depends on <i>which</i> group receives it — only that the totals
    /// reconcile — because the per-rate detail is not returned or stored.
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
            return new VatAllocation(Money.Zero, Money.Zero, Money.Zero);

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

        var nets = new Money[lines.Length];
        var vats = new Money[lines.Length];

        for (var i = 0; i < lines.Length; i++)
        {
            var multiplier = 1m + (lines[i].Rate.ToPercentage() / 100m);
            nets[i] = Money.RoundedPerBR11(shares[i].Amount / multiplier);

            // Subtraction, not a second rate calculation: this is what guarantees
            // net + vat == share for every group, and therefore for the totals.
            vats[i] = shares[i] - nets[i];
        }

        return new VatAllocation(Money.Sum(nets), Money.Sum(vats), targetGross);
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
