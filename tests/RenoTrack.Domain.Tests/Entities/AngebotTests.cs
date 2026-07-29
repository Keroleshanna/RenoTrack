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

    // ---- Full state machine: guarded transitions ---------------------------

    [Fact]
    public void SubmitForReview_OnlyAllowedFromDraft() =>
        AssertTransitionOnlyAllowedFrom(AngebotStatus.Draft, a => a.SubmitForReview(), nameof(Angebot.SubmitForReview));

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

    private static void AssertTransitionOnlyAllowedFrom(AngebotStatus expectedFrom, Action<Angebot> transition, string transitionName)
    {
        foreach (var status in Enum.GetValues<AngebotStatus>())
        {
            var angebot = CreateAngebotInStatus(status);

            if (status == expectedFrom)
            {
                var exception = Record.Exception(() => transition(angebot));
                Assert.Null(exception);
            }
            else
            {
                var exception = Assert.Throws<InvalidOperationException>(() => transition(angebot));
                Assert.Contains(transitionName, exception.Message);
                Assert.Contains(status.ToString(), exception.Message);
                Assert.Contains(expectedFrom.ToString(), exception.Message);
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
