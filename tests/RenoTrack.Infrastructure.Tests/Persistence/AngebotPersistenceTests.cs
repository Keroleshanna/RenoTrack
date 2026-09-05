using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Infrastructure.Tests.Persistence;

[Collection("Infrastructure Database")]
public sealed class AngebotPersistenceTests(RenoTrackDbContextFixture fixture)
{
    /// <summary>Angebot.LeadId is a real FK — every test needs an actually-persisted Lead row, not a hardcoded placeholder id.</summary>
    private async Task<int> SeedLeadAsync()
    {
        var lead = Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Phone);
        await using var writeContext = fixture.CreateContext();
        writeContext.Leads.Add(lead);
        await writeContext.SaveChangesAsync();
        return lead.Id;
    }

    /// <summary>Angebot.CreatedByInspectorId is a real FK as of Slice 15 — needs an actually-persisted ApplicationUser row.</summary>
    private async Task<int> SeedApplicationUserAsync()
    {
        var user = new ApplicationUser { Name = "Test Inspector" };
        await using var writeContext = fixture.CreateContext();
        writeContext.Users.Add(user);
        await writeContext.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task AddingAnAngebotWithSectionsAndItems_PersistsAndReloadsTheFullTree()
    {
        var leadId = await SeedLeadAsync();
        var inspectorId = await SeedApplicationUserAsync();
        var angebot = Angebot.Create(leadId, inspectionId: null, angebotNumber: "ANG-2026-00001", createdByInspectorId: inspectorId);
        var section = angebot.AddSection("Pos. 1 Baustelleneinrichtung", sortOrder: 1);
        angebot.AddItemToSection(section, "Bodenbelag", 13.77m, ItemUnit.SquareMeter(), Money.FromExact(18.56m), VatRate.Standard, "Feinsteinzeug");

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Angebote.Add(angebot);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Angebote
            .Include(a => a.Sections)
            .ThenInclude(s => s.Items)
            .SingleAsync(a => a.Id == angebot.Id);

        Assert.Equal(leadId, reloaded.LeadId);
        var reloadedSection = Assert.Single(reloaded.Sections);
        Assert.Equal("Pos. 1 Baustelleneinrichtung", reloadedSection.Title);
        Assert.Equal(1, reloadedSection.SortOrder);

        var reloadedItem = Assert.Single(reloadedSection.Items);
        Assert.Equal("Bodenbelag", reloadedItem.Description);
        Assert.Equal("Feinsteinzeug", reloadedItem.Specification);
        Assert.Equal(13.77m, reloadedItem.Quantity);
        Assert.Equal(ItemUnit.SquareMeter(), reloadedItem.Unit);
        Assert.Equal(Money.FromExact(18.56m), reloadedItem.UnitPrice);
        Assert.Equal(VatRate.Standard, reloadedItem.VatRate);

        // Proves Ignore()-ing the computed properties didn't break them post-reload.
        Assert.Equal(Money.RoundedPerBR11(13.77m * 18.56m), reloadedItem.LineTotal);
        Assert.Equal(reloadedItem.LineTotal, reloadedSection.Subtotal);
    }

    [Fact]
    public async Task ReloadedAngebot_NetAndGrossTotalsMatchTheOriginallyComputedValues()
    {
        var leadId = await SeedLeadAsync();
        var inspectorId = await SeedApplicationUserAsync();
        var angebot = Angebot.Create(leadId, null, "ANG-2026-00002", inspectorId);
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(100.00m), VatRate.Standard);
        var expectedNetTotal = angebot.NetTotal;
        var expectedGrossTotal = angebot.GrossTotal;

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Angebote.Add(angebot);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Angebote.SingleAsync(a => a.Id == angebot.Id);

        Assert.Equal(expectedNetTotal, reloaded.NetTotal);
        Assert.Equal(expectedGrossTotal, reloaded.GrossTotal);
    }

    [Fact]
    public async Task AngebotNumber_UniqueConstraint_RejectsADuplicate()
    {
        var inspectorId = await SeedApplicationUserAsync();
        var firstLeadId = await SeedLeadAsync();
        var first = Angebot.Create(firstLeadId, null, "ANG-2026-00003", inspectorId);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Angebote.Add(first);
            await writeContext.SaveChangesAsync();
        }

        var secondLeadId = await SeedLeadAsync();
        var duplicate = Angebot.Create(secondLeadId, null, "ANG-2026-00003", inspectorId);

        await using var duplicateContext = fixture.CreateContext();
        duplicateContext.Angebote.Add(duplicate);

        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
    }

    [Fact]
    public async Task AngebotItem_CatalogItemIdForeignKey_RejectsANonExistentCatalogItem()
    {
        var leadId = await SeedLeadAsync();
        var inspectorId = await SeedApplicationUserAsync();
        var angebot = Angebot.Create(leadId, null, "ANG-2026-00004", inspectorId);
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard, catalogItemId: 999_999);

        await using var writeContext = fixture.CreateContext();
        writeContext.Angebote.Add(angebot);

        await Assert.ThrowsAsync<DbUpdateException>(() => writeContext.SaveChangesAsync());
    }

    [Fact]
    public async Task LeadIdForeignKey_RejectsANonExistentLead()
    {
        var inspectorId = await SeedApplicationUserAsync();
        var angebot = Angebot.Create(leadId: 999_999, null, "ANG-2026-00005", inspectorId);

        await using var writeContext = fixture.CreateContext();
        writeContext.Angebote.Add(angebot);

        await Assert.ThrowsAsync<DbUpdateException>(() => writeContext.SaveChangesAsync());
    }

    [Fact]
    public async Task InspectionIdForeignKey_RejectsANonExistentInspection()
    {
        var leadId = await SeedLeadAsync();
        var inspectorId = await SeedApplicationUserAsync();
        var angebot = Angebot.Create(leadId, inspectionId: 999_999, "ANG-2026-00006", inspectorId);

        await using var writeContext = fixture.CreateContext();
        writeContext.Angebote.Add(angebot);

        await Assert.ThrowsAsync<DbUpdateException>(() => writeContext.SaveChangesAsync());
    }

    [Fact]
    public async Task CreatedByInspectorIdForeignKey_RejectsANonExistentUser()
    {
        var leadId = await SeedLeadAsync();
        var angebot = Angebot.Create(leadId, null, "ANG-2026-00007", createdByInspectorId: 999_999);

        await using var writeContext = fixture.CreateContext();
        writeContext.Angebote.Add(angebot);

        await Assert.ThrowsAsync<DbUpdateException>(() => writeContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ReviewedByAdminIdForeignKey_RejectsANonExistentUser()
    {
        var leadId = await SeedLeadAsync();
        var inspectorId = await SeedApplicationUserAsync();
        var angebot = Angebot.Create(leadId, null, "ANG-2026-00008", inspectorId);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Angebote.Add(angebot);
            await writeContext.SaveChangesAsync();

            // ReviewedByAdminId is only ever set via Approve()/RequestChanges(), both of which
            // require Status == InReview — driving it here directly via EF's change tracker is
            // the simplest way to isolate this one FK, matching the pattern already used for
            // Status in AngebotRepositoryTests.
            writeContext.Entry(angebot).Property(nameof(Angebot.ReviewedByAdminId)).CurrentValue = 999_999;
            await Assert.ThrowsAsync<DbUpdateException>(() => writeContext.SaveChangesAsync());
        }
    }

    // ---- FR-6.3's rejection reason (D98, migration #12) ----------------

    /// <summary>
    /// Drives a fresh Angebot through its real transitions to <c>Sent</c>, so a decision can be
    /// recorded without reaching past the aggregate.
    /// </summary>
    private async Task<Angebot> SeedSentAngebotAsync(string angebotNumber)
    {
        var leadId = await SeedLeadAsync();
        var inspectorId = await SeedApplicationUserAsync();
        var angebot = Angebot.Create(leadId, null, angebotNumber, inspectorId);
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Arbeit", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);
        angebot.SubmitForReview();
        angebot.Approve(inspectorId);
        angebot.Send();
        return angebot;
    }

    /// <summary>
    /// German free text with umlauts and an eszett, because the column is <c>nvarchar</c> and a
    /// <c>varchar</c> would corrupt exactly this and only this.
    /// </summary>
    [Fact]
    public async Task ADecisionReason_RoundTripsThroughTheDatabase()
    {
        var angebot = await SeedSentAngebotAsync("ANG-2026-00020");
        const string reason = "Zu teuer für unser Budget — wir müssen leider absagen. Größe passt auch nicht.";
        angebot.RecordCustomerRejection(reason);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Angebote.Add(angebot);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Angebote.SingleAsync(a => a.Id == angebot.Id);

        Assert.Equal(reason, reloaded.DecisionReason);
        Assert.Equal(AngebotStatus.CustomerRejected, reloaded.Status);
    }

    /// <summary>
    /// No sentinel: a rejection without a reason stores <c>NULL</c>, which is the same value every
    /// pre-migration row carries. That is what makes the migration need no backfill.
    /// </summary>
    [Fact]
    public async Task ARejectionWithoutAReason_StoresNull()
    {
        var angebot = await SeedSentAngebotAsync("ANG-2026-00021");
        angebot.RecordCustomerRejection();

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Angebote.Add(angebot);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var stored = await readContext.Angebote
            .Where(a => a.Id == angebot.Id)
            .Select(a => a.DecisionReason)
            .SingleAsync();

        Assert.Null(stored);
    }

    /// <summary>An approval stores no reason, all the way down to the column.</summary>
    [Fact]
    public async Task AnApproval_StoresNoReason()
    {
        var angebot = await SeedSentAngebotAsync("ANG-2026-00022");
        angebot.RecordCustomerApproval();

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Angebote.Add(angebot);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var stored = await readContext.Angebote
            .Where(a => a.Id == angebot.Id)
            .Select(a => a.DecisionReason)
            .SingleAsync();

        Assert.Null(stored);
    }

    /// <summary>
    /// Exactly at the limit, stored whole. A column narrower than the Domain guard would truncate
    /// silently here rather than fail, which is why this asserts the length back rather than only
    /// that the write succeeded.
    /// </summary>
    [Fact]
    public async Task AReasonAtExactlyTheLimit_IsStoredWhole()
    {
        var angebot = await SeedSentAngebotAsync("ANG-2026-00023");
        var atLimit = new string('ä', Angebot.MaxDecisionReasonLength);
        angebot.RecordCustomerRejection(atLimit);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Angebote.Add(angebot);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var stored = await readContext.Angebote
            .Where(a => a.Id == angebot.Id)
            .Select(a => a.DecisionReason)
            .SingleAsync();

        Assert.Equal(Angebot.MaxDecisionReasonLength, stored!.Length);
        Assert.Equal(atLimit, stored);
    }

    /// <summary>
    /// The column is the backstop behind the Domain guard. An over-length reason cannot reach here
    /// through the aggregate at all — <c>RecordCustomerRejection</c> throws first — so this drives
    /// raw SQL to prove that if one ever did, SQL Server <b>rejects</b> it rather than truncating.
    /// A silently shortened reason would misquote the customer, which is worse than no reason.
    /// </summary>
    [Fact]
    public async Task AReasonLongerThanTheColumn_IsRejectedByTheDatabase()
    {
        var angebot = await SeedSentAngebotAsync("ANG-2026-00024");
        angebot.RecordCustomerRejection("Kurz.");

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Angebote.Add(angebot);
            await writeContext.SaveChangesAsync();
        }

        var tooLong = new string('x', Angebot.MaxDecisionReasonLength + 1);

        await using var context = fixture.CreateContext();
        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlAsync(
            $"UPDATE Angebote SET DecisionReason = {tooLong} WHERE Id = {angebot.Id}"));

        // And the stored value is untouched by the refused write.
        await using var readContext = fixture.CreateContext();
        var stored = await readContext.Angebote
            .Where(a => a.Id == angebot.Id)
            .Select(a => a.DecisionReason)
            .SingleAsync();

        Assert.Equal("Kurz.", stored);
    }
}
