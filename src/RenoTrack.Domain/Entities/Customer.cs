namespace RenoTrack.Domain.Entities;

/// <summary>
/// The Lead's contact details, promoted into their own record once the customer has committed
/// to the work (Architecture.md §6 lists "Customer (root)"; ERD.md: "One Customer per Lead —
/// created at Project-conversion time"). Owns no children.
///
/// <para>
/// <b>Relates to its Lead by id only.</b> There is no navigation property to <see cref="Lead"/>
/// as a type — CLAUDE.md §2's rule for independent aggregates, the same treatment
/// <c>Angebot.LeadId</c> and <c>Inspection.LeadId</c> already get.
/// </para>
/// <para>
/// <b>Why the fields are copied rather than read through to the Lead.</b> ERD.md gives Customers
/// its own <c>Name</c>/<c>Address</c>/<c>Email</c>/<c>Phone</c> columns rather than deriving them,
/// and FR-7.1 describes conversion as "carrying over the customer's details". The same
/// copy-at-creation reasoning as BR-8 applies: once work is committed, later edits to the
/// originating Lead must not silently rewrite who the Project was agreed with.
/// </para>
/// <para>
/// <b><see cref="Address"/> is nullable, deliberately.</b> <c>Lead.Address</c> is optional — the
/// public website contact form (Sequence Diagram §1) does not collect one — so requiring an
/// address here would block conversion of an otherwise perfectly valid <c>CustomerApproved</c>
/// Angebot, inventing a business rule no document states. ERD.md was corrected to match rather
/// than the Domain being bent to fit an unexamined non-null default.
/// </para>
/// <para>
/// <b>The address can be corrected, and nothing else can (Phase 14 Slice 2b, D112).</b> BR-5 puts
/// the customer's address on every invoice, and a Customer converted from a website Lead has none —
/// so <see cref="CorrectAddress"/> is this aggregate's one mutator. The copy-at-creation guarantee
/// above is about the <i>source</i>: later edits to the Lead never leak into a committed Customer.
/// It never meant the Customer itself could not be deliberately corrected.
/// </para>
/// </summary>
public sealed class Customer
{
    /// <summary>
    /// The longest address a Customer may hold — 500 characters, read by
    /// <see cref="CorrectAddress"/>, the command validator and the EF column alike (the D109 shape).
    /// </summary>
    /// <remarks>
    /// <b>Owned here, not borrowed from <c>Lead</c></b> (D112). It is a Customer.Address constraint
    /// and this aggregate references no other aggregate's type. Conversion copies a Lead's address
    /// into a Customer, so <c>Lead.MaxAddressLength</c> must never exceed this value; that is pinned
    /// by a test rather than by a reference between the two aggregates.
    /// </remarks>
    public const int MaxAddressLength = 500;

    public int Id { get; private set; }
    public int LeadId { get; private set; }
    public string Name { get; private set; }
    public string Email { get; private set; }
    public string Phone { get; private set; }
    public string? Address { get; private set; }

    /// <summary>
    /// Assignment only — every guard lives in <see cref="Create"/> (CLAUDE.md §2). EF Core
    /// materialises a persisted row through this same private constructor, so a guard placed here
    /// would run on every read; keeping them in the factory is what stops a rule from being
    /// re-evaluated against a row that was legitimately created under it.
    /// </summary>
    private Customer(int leadId, string name, string email, string phone, string? address)
    {
        LeadId = leadId;
        Name = name;
        Email = email;
        Phone = phone;
        Address = address;
    }

    /// <summary>
    /// The only way to bring a Customer into existence. Every guard here is a self-guard in the
    /// CLAUDE.md §2 sense — determinable from the arguments alone, with no repository or other
    /// aggregate involved. In particular this does <i>not</i> check that
    /// <paramref name="leadId"/> names a real Lead, or that no Customer already exists for it:
    /// both require a query, so both belong to the Application layer (and, for uniqueness, to the
    /// unique index ERD.md specifies on <c>Customers.LeadId</c>).
    ///
    /// <para>
    /// <paramref name="name"/>, <paramref name="email"/> and <paramref name="phone"/> are required
    /// because <c>Lead.Create</c> already requires all three, so a Lead can never reach conversion
    /// without them — this mirrors an invariant that is already true rather than adding one.
    /// <paramref name="address"/> is optional for the same reason in reverse.
    /// </para>
    /// </summary>
    public static Customer Create(int leadId, string name, string email, string phone, string? address = null)
    {
        if (leadId <= 0)
            throw new ArgumentException("Lead id must be positive.", nameof(leadId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Customer name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Customer email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(phone))
            throw new ArgumentException("Customer phone is required.", nameof(phone));

        return new Customer(leadId, name.Trim(), email.Trim(), phone.Trim(), address?.Trim());
    }

    /// <summary>
    /// Sets or corrects the customer's address (BR-5, SRS FR-7.5, D112). Admin-only — that is
    /// enforced by the API, not here: the Domain never encodes who is calling (CLAUDE.md §2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required, so it can set or correct but never clear.</b> The address exists on this
    /// aggregate to be printed on an invoice; clearing it has no documented use and would only make
    /// invoices unrenderable again.
    /// </para>
    /// <para>
    /// Trimmed, and the length is measured after trimming, because trimming is what is stored. These
    /// are lifetime invariants of a corrected address, so they live in this method; the constructor
    /// EF Core materialises rows through stays guard-free, so a historical row with no address — or
    /// with the empty string an all-whitespace Lead address produced — still loads.
    /// </para>
    /// <para>
    /// No other field changes. Name, email and phone are not correctable (D112).
    /// </para>
    /// </remarks>
    public void CorrectAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Customer address is required.", nameof(address));

        var trimmed = address.Trim();

        if (trimmed.Length > MaxAddressLength)
        {
            throw new ArgumentException(
                $"Customer address must be at most {MaxAddressLength} characters.",
                nameof(address));
        }

        Address = trimmed;
    }
}
