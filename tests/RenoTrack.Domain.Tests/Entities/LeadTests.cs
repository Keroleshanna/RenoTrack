using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Domain.Tests.Entities;

public class LeadTests
{
    private const string ValidName = "Jane Doe";
    private const string ValidPhone = "0176 1234567";
    private const string ValidEmail = "jane@example.com";

    // ---- Create -------------------------------------------------------

    [Fact]
    public void Create_InitializesStatusAsNew()
    {
        var lead = Lead.Create(ValidName, ValidPhone, ValidEmail, LeadSource.Website);

        Assert.Equal(LeadStatus.New, lead.Status);
    }

    [Fact]
    public void Create_SetsAssignedInspectorIdToNull()
    {
        var lead = Lead.Create(ValidName, ValidPhone, ValidEmail, LeadSource.Website);

        Assert.Null(lead.AssignedInspectorId);
    }

    [Theory]
    [InlineData(LeadSource.Website)]
    [InlineData(LeadSource.Phone)]
    [InlineData(LeadSource.Email)]
    public void Create_PreservesProvidedValues(LeadSource source)
    {
        var lead = Lead.Create(
            ValidName, ValidPhone, ValidEmail, source,
            address: "Musterstr. 1, 12345 Berlin",
            notes: "Wants the bathroom retiled, ~10m2");

        Assert.Equal(ValidName, lead.Name);
        Assert.Equal(ValidPhone, lead.Phone);
        Assert.Equal(ValidEmail, lead.Email);
        Assert.Equal(source, lead.Source);
        Assert.Equal("Musterstr. 1, 12345 Berlin", lead.Address);
        Assert.Equal("Wants the bathroom retiled, ~10m2", lead.Notes);
    }

    [Fact]
    public void Create_AllowsOmittingAddressAndNotes()
    {
        var lead = Lead.Create(ValidName, ValidPhone, ValidEmail, LeadSource.Website);

        Assert.Null(lead.Address);
        Assert.Null(lead.Notes);
    }

    [Theory]
    [InlineData("", ValidPhone, ValidEmail)]
    [InlineData("   ", ValidPhone, ValidEmail)]
    [InlineData(ValidName, "", ValidEmail)]
    [InlineData(ValidName, "   ", ValidEmail)]
    [InlineData(ValidName, ValidPhone, "")]
    [InlineData(ValidName, ValidPhone, "   ")]
    public void Create_RejectsMissingRequiredFields(string name, string phone, string email)
    {
        Assert.Throws<ArgumentException>(() => Lead.Create(name, phone, email, LeadSource.Website));
    }

    // ---- Status transitions --------------------------------------------
    // Each transition method must succeed only when Lead is currently in its documented
    // "From" status (StateMachine.md §1.3), and reject every other status with a message
    // that names both the actual and the expected status.

    [Fact]
    public void MarkInspectionScheduled_OnlyAllowedFromNew() =>
        AssertTransitionOnlyAllowedFrom(LeadStatus.New, l => l.MarkInspectionScheduled(), nameof(Lead.MarkInspectionScheduled));

    [Fact]
    public void MarkInspectionDone_OnlyAllowedFromInspectionScheduled() =>
        AssertTransitionOnlyAllowedFrom(LeadStatus.InspectionScheduled, l => l.MarkInspectionDone(), nameof(Lead.MarkInspectionDone));

    [Fact]
    public void MarkAngebotInProgress_OnlyAllowedFromInspectionDone() =>
        AssertTransitionOnlyAllowedFrom(LeadStatus.InspectionDone, l => l.MarkAngebotInProgress(), nameof(Lead.MarkAngebotInProgress));

    [Fact]
    public void MarkAngebotSent_OnlyAllowedFromAngebotInProgress() =>
        AssertTransitionOnlyAllowedFrom(LeadStatus.AngebotInProgress, l => l.MarkAngebotSent(), nameof(Lead.MarkAngebotSent));

    [Fact]
    public void MarkWon_OnlyAllowedFromAngebotSent() =>
        AssertTransitionOnlyAllowedFrom(LeadStatus.AngebotSent, l => l.MarkWon(), nameof(Lead.MarkWon));

    [Fact]
    public void MarkLost_OnlyAllowedFromAngebotSent() =>
        AssertTransitionOnlyAllowedFrom(LeadStatus.AngebotSent, l => l.MarkLost(), nameof(Lead.MarkLost));

    // ---- AssignInspector -------------------------------------------------
    // Administrative action, deliberately independent of LeadStatus (Architecture.md §6.2).

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void AssignInspector_WorksFromAnyStatusAndLeavesStatusUnchanged(LeadStatus status)
    {
        var lead = CreateLeadInStatus(status);

        lead.AssignInspector(42);

        Assert.Equal(42, lead.AssignedInspectorId);
        Assert.Equal(status, lead.Status);
    }

    public static IEnumerable<object[]> AllStatuses() =>
        Enum.GetValues<LeadStatus>().Select(status => new object[] { status });

    // ---- Test helpers ------------------------------------------------

    /// <summary>
    /// Drives a fresh Lead through the real transition methods to reach the requested status —
    /// deliberately not using reflection/backdoors, so these tests exercise the same guarded
    /// API production code would use.
    /// </summary>
    private static Lead CreateLeadInStatus(LeadStatus status)
    {
        var lead = Lead.Create(ValidName, ValidPhone, ValidEmail, LeadSource.Website);
        if (status == LeadStatus.New) return lead;

        lead.MarkInspectionScheduled();
        if (status == LeadStatus.InspectionScheduled) return lead;

        lead.MarkInspectionDone();
        if (status == LeadStatus.InspectionDone) return lead;

        lead.MarkAngebotInProgress();
        if (status == LeadStatus.AngebotInProgress) return lead;

        lead.MarkAngebotSent();
        if (status == LeadStatus.AngebotSent) return lead;

        switch (status)
        {
            case LeadStatus.Won:
                lead.MarkWon();
                return lead;
            case LeadStatus.Lost:
                lead.MarkLost();
                return lead;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unreachable LeadStatus in test helper.");
        }
    }

    /// <summary>
    /// For every possible LeadStatus: asserts the transition succeeds when the Lead is
    /// currently in <paramref name="expectedFrom"/>, and throws InvalidOperationException
    /// naming both the actual and expected status for every other status.
    /// </summary>
    private static void AssertTransitionOnlyAllowedFrom(LeadStatus expectedFrom, Action<Lead> transition, string transitionName)
    {
        foreach (var status in Enum.GetValues<LeadStatus>())
        {
            var lead = CreateLeadInStatus(status);

            if (status == expectedFrom)
            {
                var exception = Record.Exception(() => transition(lead));
                Assert.Null(exception);
            }
            else
            {
                var exception = Assert.Throws<InvalidOperationException>(() => transition(lead));
                Assert.Contains(transitionName, exception.Message);
                Assert.Contains(status.ToString(), exception.Message);
                Assert.Contains(expectedFrom.ToString(), exception.Message);
            }
        }
    }
}
