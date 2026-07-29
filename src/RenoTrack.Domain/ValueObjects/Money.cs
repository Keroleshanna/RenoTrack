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

    public static Money Sum(IEnumerable<Money> values) => values.Aggregate(Zero, (acc, m) => acc + m);
}
