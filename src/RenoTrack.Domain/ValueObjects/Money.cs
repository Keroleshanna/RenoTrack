namespace RenoTrack.Domain.ValueObjects;

/// <summary>
/// A monetary amount, always exact to two decimal places — that exactness is an intrinsic
/// invariant of Money itself (a fact about what a Euro amount is), not a business rule.
///
/// How a raw, arbitrary-precision calculation result gets rounded down to that exact
/// representation is a separate, named concern: see <see cref="RoundedPerBR11"/>. Keeping that
/// policy in its own explicitly-named factory (rather than baking a single implicit rounding
/// behavior into every way of creating Money) means a future different rounding policy for a
/// different financial workflow can be added as a second, equally explicit factory without
/// touching this type or any existing call site that already relies on BR-11.
/// </summary>
public sealed record Money
{
    public decimal Amount { get; }

    private Money(decimal amount)
    {
        var exact = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (exact != amount)
            throw new ArgumentException($"Money amount must already be exact to 2 decimal places; got {amount}.", nameof(amount));

        Amount = amount;
    }

    public static Money Zero { get; } = new(0m);

    /// <summary>Wraps a value already known to be exact to 2 decimal places (e.g. a price entered directly by a user).</summary>
    public static Money FromExact(decimal exactAmount) => new(exactAmount);

    /// <summary>
    /// BR-11: rounds an arbitrary-precision raw calculation result (e.g. Quantity × UnitPrice,
    /// or a VAT-rate percentage applied to a net amount) to a valid Money value, using
    /// <see cref="MidpointRounding.AwayFromZero"/>. This is the only place BR-11's specific
    /// rounding rule is implemented.
    /// </summary>
    public static Money RoundedPerBR11(decimal rawAmount) =>
        new(decimal.Round(rawAmount, 2, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Adding two already-rounded Money values never produces new fractional precision, so no
    /// re-rounding is applied here (BR-11: "section totals... from already-rounded line totals
    /// — no further rounding").
    /// </summary>
    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);

    /// <summary>
    /// The exact mirror of <see cref="op_Addition"/>: subtracting one already-rounded Money from
    /// another cannot produce fractional precision either, so no rounding step is applied here.
    /// Introduced in Phase 8 for BR-3's remaining-balance figure (Project.AgreedTotal minus the
    /// gross of every non-Void Invoice, Sequence Diagram §8).
    ///
    /// <para>
    /// <b>A negative result is legal and load-bearing.</b> BR-3 says the system "warns (does not
    /// hard-block)" when invoices do not sum to the agreed total, so over-invoicing is a state the
    /// system must be able to represent and display — a remaining balance of −€250.00 is the
    /// warning. Clamping at zero here would silently hide exactly the data-entry mistake BR-3
    /// exists to surface. Money has never forbidden negative amounts; where a negative is
    /// meaningless, the aggregate says so itself (<c>Project.Create</c>, <c>Invoice.Create</c>).
    /// </para>
    /// </summary>
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount);

    public static Money Sum(IEnumerable<Money> values) => values.Aggregate(Zero, (acc, m) => acc + m);
}
