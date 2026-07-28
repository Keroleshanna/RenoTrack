using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Entities;

/// <summary>
/// Aggregate root for a quote (Architecture.md §6) → AngebotSection (child) → AngebotItem
/// (grandchild). References <see cref="LeadId"/>/<see cref="InspectionId"/>/
/// <see cref="CreatedByInspectorId"/>/<see cref="ReviewedByAdminId"/> by id only, with no
/// navigation properties — same pattern as every other aggregate root in this Domain.
///
/// <see cref="AngebotNumber"/> is generated externally (Architecture §8's
/// <c>INumberGeneratorService</c>, an atomic DB operation) and passed in already-formed; this
/// aggregate never generates its own number.
///
/// <see cref="AngebotReviewComment"/> is deliberately not modeled here — Architecture §6's
/// aggregate diagram lists only AngebotSection as Angebot's child, so review comments are a
/// separate, Application-coordinated concern (like ContactMessage is for Lead), not part of
/// this aggregate's consistency boundary. <see cref="RequestChanges"/> therefore takes no
/// comment parameter.
/// </summary>
public sealed class Angebot
{
    private readonly List<AngebotSection> _sections = [];

    public int Id { get; private set; }
    public int LeadId { get; private set; }
    public int? InspectionId { get; private set; }
    public string AngebotNumber { get; private set; }
    public AngebotStatus Status { get; private set; }
    public int CreatedByInspectorId { get; private set; }
    public int? ReviewedByAdminId { get; private set; }
    public DateTime? SentAt { get; private set; }
    public DateTime? DecisionAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public IReadOnlyList<AngebotSection> Sections => _sections;

    /// <summary>
    /// Stored and kept current by <see cref="RecalculateTotals"/>, deliberately not a computed
    /// property — see that method's remarks for why this differs from
    /// <see cref="VatBreakdown"/>.
    /// </summary>
    public Money NetTotal { get; private set; } = Money.Zero;

    /// <summary>Stored alongside <see cref="NetTotal"/>; see <see cref="RecalculateTotals"/>.</summary>
    public Money GrossTotal { get; private set; } = Money.Zero;

    /// <summary>
    /// Always computed fresh from the live item collection, never stored — unlike
    /// <see cref="NetTotal"/>/<see cref="GrossTotal"/>. Two reasons: (1) ERD.md has no column
    /// for it at all — there is nothing to keep a stored field synchronized with, no database
    /// read-path this is denormalizing for. (2) It is grouping data (one row per distinct VAT
    /// rate actually present), not a single scalar — caching a variable-shaped collection
    /// would need its own invalidation handling for no documented benefit. NetTotal/GrossTotal
    /// earn their storage by mirroring real ERD columns used for list-page rendering
    /// (Wireframes.md B2); VatBreakdown has no equivalent to mirror.
    /// </summary>
    public IReadOnlyList<VatBreakdownLine> VatBreakdown =>
        _sections
            .SelectMany(s => s.Items)
            .GroupBy(i => i.VatRate)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var netAmount = Money.Sum(g.Select(i => i.LineTotal));
                var vatAmount = Money.RoundedPerBR11(netAmount.Amount * (g.Key.ToPercentage() / 100m));
                return new VatBreakdownLine(g.Key, netAmount, vatAmount);
            })
            .ToList();

    private Angebot(int leadId, int? inspectionId, string angebotNumber, int createdByInspectorId)
    {
        if (string.IsNullOrWhiteSpace(angebotNumber))
            throw new ArgumentException("Angebot number is required.", nameof(angebotNumber));

        LeadId = leadId;
        InspectionId = inspectionId;
        AngebotNumber = angebotNumber.Trim();
        CreatedByInspectorId = createdByInspectorId;
        Status = AngebotStatus.Draft;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates a new Angebot draft. Sequence Diagram §4. The Application layer is responsible,
    /// before calling this, for: generating <paramref name="angebotNumber"/>
    /// (<c>INumberGeneratorService</c>), confirming <c>Lead.Status == InspectionDone</c>, and
    /// confirming no other non-terminal Angebot already exists for this Lead
    /// (StateMachine.md §2.4) — none of that is checkable from this aggregate alone.
    /// </summary>
    public static Angebot Create(int leadId, int? inspectionId, string angebotNumber, int createdByInspectorId) =>
        new(leadId, inspectionId, angebotNumber, createdByInspectorId);

    /// <summary>Sequence Diagram §4 "Add a Section". See <see cref="EnsureEditable"/> for the edit-lock/ChangesRequested-reopening rule.</summary>
    public AngebotSection AddSection(string title, int sortOrder)
    {
        EnsureEditable();

        var section = new AngebotSection(title, sortOrder);
        _sections.Add(section);
        RecalculateTotals();
        return section;
    }

    /// <summary>
    /// Sequence Diagram §4 "Add a Line Item". Takes the target <see cref="AngebotSection"/>
    /// itself rather than an id: before persistence (Phase 3 assigns real database ids),
    /// freshly-created sections all share <c>Id == 0</c>, so an id-based lookup within this
    /// aggregate cannot reliably distinguish between them. The Application layer resolves
    /// which section a request targets (e.g. from a route id, once ids are real) by reading
    /// the already-loaded <see cref="Sections"/> collection, then passes the object here —
    /// this method still verifies that section actually belongs to this aggregate.
    /// </summary>
    public AngebotItem AddItemToSection(
        AngebotSection section,
        string description,
        decimal quantity,
        ItemUnit unit,
        Money unitPrice,
        VatRate vatRate,
        string? specification = null,
        int? catalogItemId = null)
    {
        EnsureEditable();

        if (!_sections.Contains(section))
            throw new InvalidOperationException($"The given section does not belong to Angebot {Id}.");

        var item = section.AddItem(description, quantity, unit, unitPrice, vatRate, specification, catalogItemId);
        RecalculateTotals();
        return item;
    }

    /// <summary>StateMachine.md §2.3: <c>Draft → InReview</c>.</summary>
    /// <exception cref="InvalidOperationException">Thrown if not currently Draft, or if no section has at least one item.</exception>
    public void SubmitForReview()
    {
        EnsureStatus(AngebotStatus.Draft, nameof(SubmitForReview));

        if (!_sections.Any(s => s.Items.Count > 0))
        {
            throw new InvalidOperationException(
                $"Cannot submit Angebot {Id} for review: it needs at least one section with at least one item.");
        }

        Status = AngebotStatus.InReview;
    }

    /// <summary>StateMachine.md §2.3: <c>InReview → ApprovedInternally</c> (Admin, internal approval — see <see cref="RecordCustomerApproval"/> for the customer's decision).</summary>
    public void Approve(int reviewedByAdminId)
    {
        EnsureStatus(AngebotStatus.InReview, nameof(Approve));
        Status = AngebotStatus.ApprovedInternally;
        ReviewedByAdminId = reviewedByAdminId;
    }

    /// <summary>
    /// StateMachine.md §2.3: <c>InReview → ChangesRequested</c>. Takes no comment — see this
    /// type's remarks on why AngebotReviewComment lives outside this aggregate.
    /// </summary>
    public void RequestChanges(int reviewedByAdminId)
    {
        EnsureStatus(AngebotStatus.InReview, nameof(RequestChanges));
        Status = AngebotStatus.ChangesRequested;
        ReviewedByAdminId = reviewedByAdminId;
    }

    /// <summary>
    /// StateMachine.md §2.3: <c>ApprovedInternally → Sent</c>. The Application layer generates
    /// the TokenLink and sends the email after this succeeds — this aggregate has no knowledge
    /// of TokenLink or email at all.
    /// </summary>
    public void Send()
    {
        EnsureStatus(AngebotStatus.ApprovedInternally, nameof(Send));
        Status = AngebotStatus.Sent;
        SentAt = DateTime.UtcNow;
    }

    /// <summary>StateMachine.md §2.3: <c>Sent → CustomerApproved</c> (terminal). Application validates the TokenLink (BR-4) before calling this.</summary>
    public void RecordCustomerApproval()
    {
        EnsureStatus(AngebotStatus.Sent, nameof(RecordCustomerApproval));
        Status = AngebotStatus.CustomerApproved;
        DecisionAt = DateTime.UtcNow;
    }

    /// <summary>StateMachine.md §2.3: <c>Sent → CustomerRejected</c> (terminal). Same TokenLink precondition as <see cref="RecordCustomerApproval"/>.</summary>
    public void RecordCustomerRejection()
    {
        EnsureStatus(AngebotStatus.Sent, nameof(RecordCustomerRejection));
        Status = AngebotStatus.CustomerRejected;
        DecisionAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Architecture.md §6.1 steps 3–5, enforcing BR-11. Private and called at the end of every
    /// method that changes the section/item tree, so there is no public way to leave NetTotal/
    /// GrossTotal out of sync with the current tree — not merely disciplined, structurally
    /// impossible from outside this type.
    /// </summary>
    private void RecalculateTotals()
    {
        NetTotal = Money.Sum(_sections.Select(s => s.Subtotal));
        GrossTotal = NetTotal + Money.Sum(VatBreakdown.Select(line => line.VatAmount));
    }

    /// <summary>
    /// StateMachine.md §2.4 invariant: editable only while Draft or ChangesRequested. Per
    /// StateMachine.md §2.3's ChangesRequested row ("Angebot moves back to Draft the moment
    /// editing resumes"), resuming an edit while ChangesRequested is itself the (undocumented
    /// as a separate named event) transition back to Draft — not a separate action the caller
    /// must invoke first.
    /// </summary>
    private void EnsureEditable()
    {
        if (Status == AngebotStatus.ChangesRequested)
        {
            Status = AngebotStatus.Draft;
            return;
        }

        if (Status != AngebotStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Cannot edit Angebot {Id}: status is '{Status}', editing is only allowed while Draft or ChangesRequested.");
        }
    }

    private void EnsureStatus(AngebotStatus expected, string actionName)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"Cannot perform '{actionName}': Angebot {Id} is in status '{Status}', expected '{expected}'.");
        }
    }
}
