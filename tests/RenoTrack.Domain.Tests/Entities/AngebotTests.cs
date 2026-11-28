using System.Reflection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Tests.Entities;

public class AngebotTests
{
    private const string ValidNumber = "ANG-2026-00001";

    // ---- Create -------------------------------------------------------

    [Fact]
    public void Create_SetsStatusToDraft()
    {
        var angebot = Angebot.Create(1, inspectionId: 5, ValidNumber, createdByInspectorId: 9);

        Assert.Equal(AngebotStatus.Draft, angebot.Status);
    }

    [Fact]
    public void Create_InitializesNetAndGrossTotalToZero()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);

        Assert.Equal(Money.Zero, angebot.NetTotal);
        Assert.Equal(Money.Zero, angebot.GrossTotal);
    }

    [Fact]
    public void Create_StartsWithNoSections()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);

        Assert.Empty(angebot.Sections);
    }

    [Fact]
    public void Create_PreservesProvidedValues()
    {
        var angebot = Angebot.Create(leadId: 1, inspectionId: 5, ValidNumber, createdByInspectorId: 9);

        Assert.Equal(1, angebot.LeadId);
        Assert.Equal(5, angebot.InspectionId);
        Assert.Equal(ValidNumber, angebot.AngebotNumber);
        Assert.Equal(9, angebot.CreatedByInspectorId);
    }

    [Fact]
    public void Create_AllowsNullInspectionId()
    {
        var angebot = Angebot.Create(1, inspectionId: null, ValidNumber, 9);

        Assert.Null(angebot.InspectionId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsEmptyAngebotNumber(string emptyNumber)
    {
        Assert.Throws<ArgumentException>(() => Angebot.Create(1, 5, emptyNumber, 9));
    }

    // ---- AddSection / AddItemToSection — editing guard --------------------

    [Fact]
    public void AddSection_AddsToSectionsCollection()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);

        var section = angebot.AddSection("Pos. 1 Baustelleneinrichtung", 1);

        Assert.Single(angebot.Sections);
        Assert.Same(section, angebot.Sections[0]);
    }

    [Fact]
    public void AddSection_OnlyAllowedWhileDraftOrChangesRequested() =>
        AssertEditActionAllowedOnlyWhileEditable(a => a.AddSection("New section", 99));

    [Fact]
    public void AddSection_WhileChangesRequested_TransitionsBackToDraft()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.ChangesRequested);

        angebot.AddSection("New section", 2);

        Assert.Equal(AngebotStatus.Draft, angebot.Status);
    }

    [Fact]
    public void AddItemToSection_OnlyAllowedWhileDraftOrChangesRequested() =>
        AssertEditActionAllowedOnlyWhileEditable(a =>
            a.AddItemToSection(a.Sections[0], "Another item", 1m, ItemUnit.Piece(), Money.FromExact(5.00m), VatRate.Standard));

    [Fact]
    public void AddItemToSection_WhileChangesRequested_TransitionsBackToDraft()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.ChangesRequested);

        angebot.AddItemToSection(angebot.Sections[0], "Another item", 1m, ItemUnit.Piece(), Money.FromExact(5.00m), VatRate.Standard);

        Assert.Equal(AngebotStatus.Draft, angebot.Status);
    }

    // ---- Aggregate boundary ------------------------------------------------

    [Fact]
    public void AddItemToSection_RejectsSectionThatBelongsToAnotherAggregate()
    {
        var angebot1 = Angebot.Create(1, 5, "ANG-2026-00001", 9);
        angebot1.AddSection("Pos. 1", 1);

        var angebot2 = Angebot.Create(2, null, "ANG-2026-00002", 9);
        var sectionFromAngebot2 = angebot2.AddSection("Pos. 1", 1);

        Assert.Throws<InvalidOperationException>(() =>
            angebot1.AddItemToSection(sectionFromAngebot2, "Item", 1m, ItemUnit.Piece(), Money.FromExact(5.00m), VatRate.Standard));
    }

    // ---- Automatic financial recalculation --------------------------------

    [Fact]
    public void AddItemToSection_RecalculatesNetTotal()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        var section = angebot.AddSection("Pos. 1", 1);

        angebot.AddItemToSection(section, "Item A", 2m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard); // 20.00

        Assert.Equal(Money.FromExact(20.00m), angebot.NetTotal);
    }

    [Fact]
    public void AddItemToSection_RecalculatesNetTotal_AcrossMultipleSectionsAndItems()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        var sectionA = angebot.AddSection("Pos. 1", 1);
        var sectionB = angebot.AddSection("Pos. 2", 2);

        angebot.AddItemToSection(sectionA, "Item A", 2m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);  // 20.00
        angebot.AddItemToSection(sectionA, "Item B", 1m, ItemUnit.Piece(), Money.FromExact(5.50m), VatRate.Standard);   // 5.50
        angebot.AddItemToSection(sectionB, "Item C", 13.77m, ItemUnit.SquareMeter(), Money.FromExact(18.56m), VatRate.Standard); // 255.57

        Assert.Equal(Money.FromExact(281.07m), angebot.NetTotal);
    }

    [Fact]
    public void GrossTotal_SingleVatRate_EqualsNetPlusVat()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        var section = angebot.AddSection("Pos. 1", 1);

        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(100.00m), VatRate.Standard); // 19%

        Assert.Equal(Money.FromExact(100.00m), angebot.NetTotal);
        Assert.Equal(Money.FromExact(119.00m), angebot.GrossTotal);
    }

    [Fact]
    public void GrossTotal_MultipleVatRates_MatchesRealSampleDocumentMix()
    {
        // Mirrors BR-6's real-world evidence: a single Angebot mixing 0%, 16%, and 19% items.
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        var section = angebot.AddSection("Pos. 1", 1);

        angebot.AddItemToSection(section, "Exempt item", 1m, ItemUnit.LumpSum(), Money.FromExact(50.00m), VatRate.Zero);
        angebot.AddItemToSection(section, "Reduced-history item", 1m, ItemUnit.LumpSum(), Money.FromExact(200.00m), VatRate.Sixteen);
        angebot.AddItemToSection(section, "Standard item", 1m, ItemUnit.LumpSum(), Money.FromExact(100.00m), VatRate.Standard);

        Assert.Equal(Money.FromExact(350.00m), angebot.NetTotal);
        // VAT: 0.00 (0%) + 32.00 (16% of 200) + 19.00 (19% of 100) = 51.00
        Assert.Equal(Money.FromExact(401.00m), angebot.GrossTotal);
    }

    // ---- VatBreakdown -------------------------------------------------

    [Fact]
    public void VatBreakdown_GroupsByRate_OrderedAscending()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        var section = angebot.AddSection("Pos. 1", 1);
        // Added deliberately out of ascending order.
        angebot.AddItemToSection(section, "Standard item", 1m, ItemUnit.LumpSum(), Money.FromExact(100.00m), VatRate.Standard);
        angebot.AddItemToSection(section, "Exempt item", 1m, ItemUnit.LumpSum(), Money.FromExact(50.00m), VatRate.Zero);
        angebot.AddItemToSection(section, "Sixteen item", 1m, ItemUnit.LumpSum(), Money.FromExact(200.00m), VatRate.Sixteen);

        var rates = angebot.VatBreakdown.Select(line => line.Rate).ToArray();

        Assert.Equal([VatRate.Zero, VatRate.Sixteen, VatRate.Standard], rates);
    }

    [Fact]
    public void VatBreakdown_NetAndVatAmountsAreCorrectPerRate()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Sixteen item", 1m, ItemUnit.LumpSum(), Money.FromExact(200.00m), VatRate.Sixteen);

        var line = Assert.Single(angebot.VatBreakdown);

        Assert.Equal(VatRate.Sixteen, line.Rate);
        Assert.Equal(Money.FromExact(200.00m), line.NetAmount);
        Assert.Equal(Money.FromExact(32.00m), line.VatAmount);
    }

    [Fact]
    public void VatBreakdown_RoundsEachRateAmountPerBR11AtMidpoint()
    {
        // 12.50 net at 7% = 0.875 exactly — a genuine midpoint, proving BR-11's
        // AwayFromZero rounding is actually applied per VAT-rate amount.
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Reduced item", 1m, ItemUnit.LumpSum(), Money.FromExact(12.50m), VatRate.Reduced);

        var line = Assert.Single(angebot.VatBreakdown);

        Assert.Equal(Money.FromExact(0.88m), line.VatAmount);
    }

    // ---- SubmitForReview: item-count guard --------------------------------

    [Fact]
    public void SubmitForReview_RejectsWhenThereAreNoSections()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);

        Assert.Throws<InvalidOperationException>(angebot.SubmitForReview);
    }

    [Fact]
    public void SubmitForReview_RejectsWhenSectionsHaveNoItems()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        angebot.AddSection("Pos. 1", 1);

        Assert.Throws<InvalidOperationException>(angebot.SubmitForReview);
    }

    // ---- UpdateItem (Phase 10) ---------------------------------------------

    [Fact]
    public void UpdateItem_ChangesTheValuesAndRecalculatesTotals()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        var section = angebot.AddSection("Bad", 1);
        var item = angebot.AddItemToSection(
            section, "Fliesn verlegn", 10m, ItemUnit.SquareMeter(), Money.FromExact(50.00m), VatRate.Standard);

        angebot.UpdateItem(
            section, item, "Fliesen verlegen", 12m, ItemUnit.SquareMeter(), Money.FromExact(55.00m),
            VatRate.Standard, "Feinsteinzeug");

        Assert.Equal("Fliesen verlegen", item.Description);
        Assert.Equal("Feinsteinzeug", item.Specification);
        Assert.Equal(12m, item.Quantity);
        Assert.Equal(55.00m, item.UnitPrice.Amount);

        // The whole point: money moves, and the stored totals must move with it.
        Assert.Equal(660.00m, item.LineTotal.Amount);
        Assert.Equal(660.00m, angebot.NetTotal.Amount);
        Assert.Equal(785.40m, angebot.GrossTotal.Amount);
    }

    [Fact]
    public void UpdateItem_CanChangeTheVatRateAndTheBreakdownFollows()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        var section = angebot.AddSection("Bad", 1);
        var item = angebot.AddItemToSection(
            section, "Leistung", 1m, ItemUnit.Piece(), Money.FromExact(100.00m), VatRate.Standard);

        angebot.UpdateItem(section, item, "Leistung", 1m, ItemUnit.Piece(), Money.FromExact(100.00m), VatRate.Reduced);

        var line = Assert.Single(angebot.VatBreakdown);
        Assert.Equal(VatRate.Reduced, line.Rate);
        Assert.Equal(7.00m, line.VatAmount.Amount);
    }

    [Fact]
    public void UpdateItem_KeepsTheCatalogProvenanceLink()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        var section = angebot.AddSection("Bad", 1);
        var item = angebot.AddItemToSection(
            section, "Aus Katalog", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard,
            catalogItemId: 42);

        angebot.UpdateItem(section, item, "Angepasst", 2m, ItemUnit.Piece(), Money.FromExact(12.00m), VatRate.Standard);

        // BR-8: the link records where the line came from, not that it still matches. Clearing it
        // would destroy provenance to record a divergence BR-8 already anticipates.
        Assert.Equal(42, item.CatalogItemId);
    }

    [Theory]
    [InlineData(AngebotStatus.InReview)]
    [InlineData(AngebotStatus.ApprovedInternally)]
    [InlineData(AngebotStatus.Sent)]
    [InlineData(AngebotStatus.CustomerApproved)]
    [InlineData(AngebotStatus.CustomerRejected)]
    public void UpdateItem_IsRefusedOnceTheAngebotIsLocked(AngebotStatus status)
    {
        var angebot = CreateAngebotInStatus(status);
        var section = angebot.Sections.Single();
        var item = section.Items.Single();

        Assert.Throws<InvalidOperationException>(() => angebot.UpdateItem(
            section, item, "Neu", 1m, ItemUnit.Piece(), Money.FromExact(1.00m), VatRate.Standard));
    }

    [Fact]
    public void UpdateItem_ReopensAChangesRequestedAngebotLikeEveryOtherEdit()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.ChangesRequested);
        var section = angebot.Sections.Single();

        angebot.UpdateItem(
            section, section.Items.Single(), "Korrigiert", 1m, ItemUnit.Piece(),
            Money.FromExact(10.00m), VatRate.Standard);

        Assert.Equal(AngebotStatus.Draft, angebot.Status);
    }

    [Fact]
    public void UpdateItem_RejectsAnItemFromAnotherSection()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        var first = angebot.AddSection("Bad", 1);
        var second = angebot.AddSection("Küche", 2);
        var item = angebot.AddItemToSection(
            second, "Leistung", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);

        Assert.Throws<InvalidOperationException>(() => angebot.UpdateItem(
            first, item, "Verschoben", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard));
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("Gültig", 0)]
    [InlineData("Gültig", -1)]
    public void UpdateItem_AppliesTheSameGuardsAsCreation(string description, decimal quantity)
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        var section = angebot.AddSection("Bad", 1);
        var item = angebot.AddItemToSection(
            section, "Original", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);

        // These are lifetime invariants of a line, not creation-time conditions, so a correction is
        // held to exactly the same standard as an insertion.
        Assert.Throws<ArgumentException>(() => angebot.UpdateItem(
            section, item, description, quantity, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard));
    }

    // ---- Full state machine: guarded transitions ---------------------------

    /// <summary>
    /// <c>SubmitForReview</c> is the one transition with <b>two</b> legal source states.
    /// </summary>
    /// <remarks>
    /// <c>ChangesRequested</c> was added in Phase 10, after QA found the workflow had a dead end:
    /// an Inspector who read the Admin's comment and concluded nothing needed changing could not
    /// send the quote back, because reaching <c>Draft</c> required editing something first. The
    /// only workaround was a pointless edit made purely to satisfy a guard. Nothing was weakened —
    /// the "at least one section with at least one item" rule still applies, and §2.4 already
    /// treats <c>ChangesRequested</c> as an editable state.
    /// </remarks>
    [Fact]
    public void SubmitForReview_AllowedFromDraftAndChangesRequested() =>
        AssertTransitionOnlyAllowedFrom(
            [AngebotStatus.Draft, AngebotStatus.ChangesRequested],
            a => a.SubmitForReview(),
            nameof(Angebot.SubmitForReview));

    [Fact]
    public void Approve_OnlyAllowedFromInReview() =>
        AssertTransitionOnlyAllowedFrom(AngebotStatus.InReview, a => a.Approve(2), nameof(Angebot.Approve));

    [Fact]
    public void RequestChanges_OnlyAllowedFromInReview() =>
        AssertTransitionOnlyAllowedFrom(AngebotStatus.InReview, a => a.RequestChanges(2), nameof(Angebot.RequestChanges));

    [Fact]
    public void Send_OnlyAllowedFromApprovedInternally() =>
        AssertTransitionOnlyAllowedFrom(AngebotStatus.ApprovedInternally, a => a.Send(), nameof(Angebot.Send));

    [Fact]
    public void RecordCustomerApproval_OnlyAllowedFromSent() =>
        AssertTransitionOnlyAllowedFrom(AngebotStatus.Sent, a => a.RecordCustomerApproval(), nameof(Angebot.RecordCustomerApproval));

    [Fact]
    public void RecordCustomerRejection_OnlyAllowedFromSent() =>
        AssertTransitionOnlyAllowedFrom(AngebotStatus.Sent, a => a.RecordCustomerRejection(), nameof(Angebot.RecordCustomerRejection));

    // ---- ReturnForCorrection: InReview → Draft (D115) -----------------------

    [Fact]
    public void ReturnForCorrection_IsOnlyAllowedFromInReview() =>
        AssertTransitionOnlyAllowedFrom(AngebotStatus.InReview, a => a.ReturnForCorrection(), nameof(Angebot.ReturnForCorrection));

    [Fact]
    public void ReturnForCorrection_ReturnsToDraft_KeepsAuthorship_AndSetsNoReviewer()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.InReview);

        angebot.ReturnForCorrection();

        Assert.Equal(AngebotStatus.Draft, angebot.Status);
        Assert.Equal(9, angebot.CreatedByInspectorId);
        Assert.Null(angebot.ReviewedByAdminId);
    }

    /// <summary>A reviewer's earlier decision on a previous cycle is not erased or overwritten.</summary>
    [Fact]
    public void ReturnForCorrection_LeavesAnEarlierReviewerUntouched()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.ChangesRequested); // reviewed by Admin 2
        angebot.SubmitForReview();

        angebot.ReturnForCorrection();

        Assert.Equal(2, angebot.ReviewedByAdminId);
    }

    /// <summary>
    /// The edit lock is unchanged: the corrected Angebot is editable only because it is a Draft
    /// again, and it must go through review once more before it can be approved (BR-1).
    /// </summary>
    [Fact]
    public void ReturnForCorrection_MakesTheAngebotEditable_AndRequiresAFreshReview()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.InReview);
        var section = angebot.Sections[0];

        angebot.ReturnForCorrection();
        angebot.UpdateItem(section, section.Items[0], "Korrigiert", 2m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);

        Assert.Throws<InvalidOperationException>(() => angebot.Approve(2));
        angebot.SubmitForReview();
        angebot.Approve(2);
        Assert.Equal(AngebotStatus.ApprovedInternally, angebot.Status);
        Assert.Equal(20.00m, angebot.NetTotal.Amount);
    }

    /// <summary>
    /// No Domain method knows who is calling (CLAUDE.md §2): the method takes no parameters at all,
    /// so a role cannot have crept into its signature.
    /// </summary>
    [Fact]
    public void ReturnForCorrection_TakesNoParameters()
    {
        var method = typeof(Angebot).GetMethod(nameof(Angebot.ReturnForCorrection), BindingFlags.Public | BindingFlags.Instance)!;

        Assert.Empty(method.GetParameters());
    }

    // ---- Side effects of successful transitions ---------------------------

    [Fact]
    public void Approve_SetsReviewedByAdminId()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.InReview);

        angebot.Approve(reviewedByAdminId: 7);

        Assert.Equal(7, angebot.ReviewedByAdminId);
    }

    [Fact]
    public void RequestChanges_SetsReviewedByAdminId()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.InReview);

        angebot.RequestChanges(reviewedByAdminId: 7);

        Assert.Equal(7, angebot.ReviewedByAdminId);
    }

    [Fact]
    public void Send_SetsSentAt()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.ApprovedInternally);

        angebot.Send();

        Assert.NotNull(angebot.SentAt);
    }

    [Fact]
    public void RecordCustomerApproval_SetsDecisionAt()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.Sent);

        angebot.RecordCustomerApproval();

        Assert.NotNull(angebot.DecisionAt);
    }

    [Fact]
    public void RecordCustomerRejection_SetsDecisionAt()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.Sent);

        angebot.RecordCustomerRejection();

        Assert.NotNull(angebot.DecisionAt);
    }

    // ---- FR-6.3's optional rejection reason (D98) --------------------

    [Fact]
    public void RecordCustomerRejection_StoresTheReason()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.Sent);

        angebot.RecordCustomerRejection("Zu teuer im Vergleich zum Wettbewerb.");

        Assert.Equal("Zu teuer im Vergleich zum Wettbewerb.", angebot.DecisionReason);
    }

    [Fact]
    public void RecordCustomerRejection_TrimsTheReason()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.Sent);

        angebot.RecordCustomerRejection("   Zu teuer.\n  ");

        Assert.Equal("Zu teuer.", angebot.DecisionReason);
    }

    /// <summary>
    /// "", "   " and absent are one stored state, not three — so nothing downstream has to decide
    /// whether an empty string means "gave no reason" or "gave an empty reason".
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n ")]
    public void RecordCustomerRejection_TreatsBlankAsNoReason(string? reason)
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.Sent);

        angebot.RecordCustomerRejection(reason);

        Assert.Null(angebot.DecisionReason);
    }

    [Fact]
    public void RecordCustomerRejection_AcceptsTheReasonAtExactlyTheLimit()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.Sent);
        var atLimit = new string('x', Angebot.MaxDecisionReasonLength);

        angebot.RecordCustomerRejection(atLimit);

        Assert.Equal(atLimit, angebot.DecisionReason);
    }

    /// <summary>
    /// The Domain is the backstop, not the Application validator (§5). A caller reaching this
    /// method directly still meets the limit.
    /// </summary>
    [Fact]
    public void RecordCustomerRejection_RejectsAReasonOverTheLimit()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.Sent);
        var tooLong = new string('x', Angebot.MaxDecisionReasonLength + 1);

        var exception = Assert.Throws<ArgumentException>(() => angebot.RecordCustomerRejection(tooLong));

        Assert.Equal("reason", exception.ParamName);
        Assert.Null(angebot.DecisionReason);
        Assert.Equal(AngebotStatus.Sent, angebot.Status);
    }

    /// <summary>
    /// Trimming happens before the length check, so 1000 characters padded with whitespace is
    /// accepted rather than refused for being 1002 long.
    /// </summary>
    [Fact]
    public void RecordCustomerRejection_MeasuresTheReasonAfterTrimming()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.Sent);
        var padded = "  " + new string('x', Angebot.MaxDecisionReasonLength) + "  ";

        angebot.RecordCustomerRejection(padded);

        Assert.Equal(Angebot.MaxDecisionReasonLength, angebot.DecisionReason!.Length);
    }

    /// <summary>An approval has nothing to justify, and D98 keeps it that way structurally.</summary>
    [Fact]
    public void RecordCustomerApproval_NeverStoresAReason()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.Sent);

        angebot.RecordCustomerApproval();

        Assert.Null(angebot.DecisionReason);
    }

    /// <summary>
    /// D98's structural half of "an approval carries no reason": the API's 400 is the friendly
    /// refusal, but the aggregate offers no overload that could store one even if a caller wanted
    /// to. Asserted by reflection, per §14, because a comment is not a guarantee.
    /// </summary>
    [Fact]
    public void RecordCustomerApproval_HasNoOverloadThatCouldTakeAReason()
    {
        var overloads = typeof(Angebot)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == nameof(Angebot.RecordCustomerApproval))
            .ToList();

        var overload = Assert.Single(overloads);
        Assert.Empty(overload.GetParameters());
    }

    /// <summary>
    /// The reason is immutable once recorded (D98): the decision is terminal and single-use under
    /// BR-4, so there is no member that can change or clear it. Reflection rather than a comment,
    /// and it names what it is looking for so a future `ClearDecisionReason` fails here first.
    /// </summary>
    [Fact]
    public void DecisionReason_HasNoPublicWriterOfAnyKind()
    {
        var setter = typeof(Angebot).GetProperty(nameof(Angebot.DecisionReason))!.SetMethod;
        Assert.NotNull(setter);
        Assert.False(setter.IsPublic, "DecisionReason must not have a public setter.");

        // IsSpecialName excludes the property's own accessors — get_DecisionReason is a public
        // method whose name contains "DecisionReason", and reading it is exactly what the
        // Dashboard needs. What must not exist is a real method that writes it.
        var mutators = typeof(Angebot)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => !method.IsSpecialName)
            .Where(method => method.Name.Contains("DecisionReason", StringComparison.Ordinal))
            .Select(method => method.Name)
            .ToList();

        Assert.Empty(mutators);
    }

    // ---- Version: the concurrency counter (D115) ----------------------------

    /// <summary>
    /// <b>The inventory.</b> Every public, non-accessor instance method of <see cref="Angebot"/> that
    /// is not a read must increment <c>Version</c>. This list names them all, and the reflection
    /// below fails the moment a method is added that is not on it — so a new mutator cannot silently
    /// escape the concurrency token by forgetting <c>IncrementVersion()</c>.
    /// </summary>
    private static readonly string[] VersionedMutators =
    [
        nameof(Angebot.AddSection),
        nameof(Angebot.AddItemToSection),
        nameof(Angebot.UpdateItem),
        nameof(Angebot.RemoveItem),
        nameof(Angebot.RemoveSection),
        nameof(Angebot.SubmitForReview),
        nameof(Angebot.Approve),
        nameof(Angebot.RequestChanges),
        nameof(Angebot.ReturnForCorrection),
        nameof(Angebot.Send),
        nameof(Angebot.RecordCustomerApproval),
        nameof(Angebot.RecordCustomerRejection),
    ];

    [Fact]
    public void Version_TheInventoryCoversEveryPublicMethod()
    {
        var publicMethods = typeof(Angebot)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Distinct()
            .Order()
            .ToList();

        Assert.Equal(VersionedMutators.Order().ToList(), publicMethods);
    }

    [Fact]
    public void Version_HasNoPublicSetter()
    {
        var setter = typeof(Angebot).GetProperty(nameof(Angebot.Version))!.SetMethod;

        Assert.NotNull(setter);
        Assert.False(setter.IsPublic);
    }

    [Fact]
    public void Version_StartsAtZero()
    {
        Assert.Equal(0, Angebot.Create(1, null, ValidNumber, 9).Version);
    }

    /// <summary>One case per inventoried mutator, each from a state where it succeeds.</summary>
    public static TheoryData<string> SuccessfulMutations() => new(VersionedMutators);

    [Theory]
    [MemberData(nameof(SuccessfulMutations))]
    public void Version_EachMutatorIncrementsItExactlyOnce(string mutator)
    {
        var (angebot, act) = ArrangeSuccessful(mutator);
        var before = angebot.Version;

        act();

        Assert.Equal(before + 1, angebot.Version);
    }

    /// <summary>A refused call leaves the version untouched — the increment runs only after the guards.</summary>
    [Theory]
    [MemberData(nameof(SuccessfulMutations))]
    public void Version_ARefusedMutatorLeavesItUnchanged(string mutator)
    {
        var (angebot, act) = ArrangeRefused(mutator);
        var before = angebot.Version;

        Assert.ThrowsAny<Exception>(act);

        Assert.Equal(before, angebot.Version);
    }

    /// <summary>
    /// An edit that moves no money still increments, which is the whole point: it writes no total,
    /// so without the counter it would write nothing to the Angebot row at all.
    /// </summary>
    [Fact]
    public void Version_ADescriptionOnlyEditStillIncrements()
    {
        var angebot = CreateAngebotInStatus(AngebotStatus.Draft);
        var section = angebot.Sections[0];
        var item = section.Items[0];
        var (net, gross, before) = (angebot.NetTotal, angebot.GrossTotal, angebot.Version);

        angebot.UpdateItem(section, item, "Nur der Text", item.Quantity, item.Unit, item.UnitPrice, item.VatRate);

        Assert.Equal(net, angebot.NetTotal);
        Assert.Equal(gross, angebot.GrossTotal);
        Assert.Equal(before + 1, angebot.Version);
    }

    private static (Angebot Angebot, Action Act) ArrangeSuccessful(string mutator)
    {
        switch (mutator)
        {
            case nameof(Angebot.AddSection):
            {
                var a = CreateAngebotInStatus(AngebotStatus.Draft);
                return (a, () => a.AddSection("Pos. 2", 2));
            }
            case nameof(Angebot.AddItemToSection):
            {
                var a = CreateAngebotInStatus(AngebotStatus.Draft);
                return (a, () => a.AddItemToSection(a.Sections[0], "Zweite", 1m, ItemUnit.Piece(), Money.FromExact(5.00m), VatRate.Standard));
            }
            case nameof(Angebot.UpdateItem):
            {
                var a = CreateAngebotInStatus(AngebotStatus.Draft);
                var s = a.Sections[0];
                return (a, () => a.UpdateItem(s, s.Items[0], "Neu", 3m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard));
            }
            case nameof(Angebot.RemoveItem):
            {
                var a = CreateAngebotInStatus(AngebotStatus.Draft);
                var s = a.Sections[0];
                return (a, () => a.RemoveItem(s, s.Items[0]));
            }
            case nameof(Angebot.RemoveSection):
            {
                var a = CreateAngebotInStatus(AngebotStatus.Draft);
                return (a, () => a.RemoveSection(a.Sections[0]));
            }
            case nameof(Angebot.SubmitForReview):
            {
                var a = CreateAngebotInStatus(AngebotStatus.Draft);
                return (a, a.SubmitForReview);
            }
            case nameof(Angebot.Approve):
            {
                var a = CreateAngebotInStatus(AngebotStatus.InReview);
                return (a, () => a.Approve(2));
            }
            case nameof(Angebot.RequestChanges):
            {
                var a = CreateAngebotInStatus(AngebotStatus.InReview);
                return (a, () => a.RequestChanges(2));
            }
            case nameof(Angebot.ReturnForCorrection):
            {
                var a = CreateAngebotInStatus(AngebotStatus.InReview);
                return (a, a.ReturnForCorrection);
            }
            case nameof(Angebot.Send):
            {
                var a = CreateAngebotInStatus(AngebotStatus.ApprovedInternally);
                return (a, a.Send);
            }
            case nameof(Angebot.RecordCustomerApproval):
            {
                var a = CreateAngebotInStatus(AngebotStatus.Sent);
                return (a, a.RecordCustomerApproval);
            }
            case nameof(Angebot.RecordCustomerRejection):
            {
                var a = CreateAngebotInStatus(AngebotStatus.Sent);
                return (a, () => a.RecordCustomerRejection("zu teuer"));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(mutator), mutator, "Not in the inventory.");
        }
    }

    /// <summary>The same mutators, each from <c>CustomerApproved</c> — a terminal state that refuses all of them.</summary>
    private static (Angebot Angebot, Action Act) ArrangeRefused(string mutator)
    {
        var a = CreateAngebotInStatus(AngebotStatus.CustomerApproved);
        var s = a.Sections[0];

        Action act = mutator switch
        {
            nameof(Angebot.AddSection) => () => a.AddSection("Pos. 2", 2),
            nameof(Angebot.AddItemToSection) => () => a.AddItemToSection(s, "X", 1m, ItemUnit.Piece(), Money.FromExact(1.00m), VatRate.Standard),
            nameof(Angebot.UpdateItem) => () => a.UpdateItem(s, s.Items[0], "X", 1m, ItemUnit.Piece(), Money.FromExact(1.00m), VatRate.Standard),
            nameof(Angebot.RemoveItem) => () => a.RemoveItem(s, s.Items[0]),
            nameof(Angebot.RemoveSection) => () => a.RemoveSection(s),
            nameof(Angebot.SubmitForReview) => a.SubmitForReview,
            nameof(Angebot.Approve) => () => a.Approve(2),
            nameof(Angebot.RequestChanges) => () => a.RequestChanges(2),
            nameof(Angebot.ReturnForCorrection) => a.ReturnForCorrection,
            nameof(Angebot.Send) => a.Send,
            nameof(Angebot.RecordCustomerApproval) => a.RecordCustomerApproval,
            nameof(Angebot.RecordCustomerRejection) => () => a.RecordCustomerRejection(),
            _ => throw new ArgumentOutOfRangeException(nameof(mutator), mutator, "Not in the inventory."),
        };

        return (a, act);
    }

    // ---- Test helpers ------------------------------------------------

    /// <summary>
    /// Drives a fresh Angebot (always with one section containing one item, so
    /// SubmitForReview's item-count guard never blocks the walk) through the real transition
    /// methods to reach the requested status.
    /// </summary>
    private static Angebot CreateAngebotInStatus(AngebotStatus status)
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);

        if (status == AngebotStatus.Draft) return angebot;

        angebot.SubmitForReview();
        if (status == AngebotStatus.InReview) return angebot;

        if (status == AngebotStatus.ChangesRequested)
        {
            angebot.RequestChanges(reviewedByAdminId: 2);
            return angebot;
        }

        angebot.Approve(reviewedByAdminId: 2);
        if (status == AngebotStatus.ApprovedInternally) return angebot;

        angebot.Send();
        if (status == AngebotStatus.Sent) return angebot;

        switch (status)
        {
            case AngebotStatus.CustomerApproved:
                angebot.RecordCustomerApproval();
                return angebot;
            case AngebotStatus.CustomerRejected:
                angebot.RecordCustomerRejection();
                return angebot;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unreachable AngebotStatus in test helper.");
        }
    }

    private static void AssertTransitionOnlyAllowedFrom(
        AngebotStatus expectedFrom,
        Action<Angebot> transition,
        string transitionName) =>
        AssertTransitionOnlyAllowedFrom([expectedFrom], transition, transitionName);

    /// <summary>
    /// Drives the aggregate to <b>every</b> status through its own real transitions, then asserts
    /// the given event succeeds from exactly the listed source states and throws from all others,
    /// naming both the actual and the expected state.
    /// </summary>
    /// <remarks>
    /// Takes a set rather than a single state so a transition with more than one legal source can
    /// still be pinned exhaustively — the alternative, exempting such a transition from this sweep,
    /// would leave the states it must still refuse untested.
    /// </remarks>
    private static void AssertTransitionOnlyAllowedFrom(
        AngebotStatus[] expectedFrom,
        Action<Angebot> transition,
        string transitionName)
    {
        foreach (var status in Enum.GetValues<AngebotStatus>())
        {
            var angebot = CreateAngebotInStatus(status);

            if (expectedFrom.Contains(status))
            {
                var exception = Record.Exception(() => transition(angebot));
                Assert.Null(exception);
            }
            else
            {
                var exception = Assert.Throws<InvalidOperationException>(() => transition(angebot));
                Assert.Contains(transitionName, exception.Message);
                Assert.Contains(status.ToString(), exception.Message);
                Assert.All(expectedFrom, allowed => Assert.Contains(allowed.ToString(), exception.Message));
            }
        }
    }

    private static void AssertEditActionAllowedOnlyWhileEditable(Action<Angebot> action)
    {
        foreach (var status in Enum.GetValues<AngebotStatus>())
        {
            var angebot = CreateAngebotInStatus(status);

            if (status is AngebotStatus.Draft or AngebotStatus.ChangesRequested)
            {
                var exception = Record.Exception(() => action(angebot));
                Assert.Null(exception);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => action(angebot));
            }
        }
    }
}
