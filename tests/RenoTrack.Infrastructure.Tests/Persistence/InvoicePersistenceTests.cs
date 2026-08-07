using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Real SQL constraints behind <c>InvoiceConfiguration</c> and <c>PaymentConfiguration</c>, against
/// real LocalDB (D40): the unique invoice number (BR-9), the Project FK and its deliberately
/// non-unique cardinality, string-stored enums, <c>decimal(18,2)</c> round-tripping of three
/// monetary columns on a legal document, and — the one thing that could not be settled by
/// inspection — that EF Core can materialise a <c>Payment</c> through its <c>internal</c>
/// constructor with a value-converted <see cref="Money"/> parameter.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class InvoicePersistenceTests(RenoTrackDbContextFixture fixture)
{
    private static readonly Money Net = Money.FromExact(6_722.69m);
    private static readonly Money Vat = Money.FromExact(1_277.31m);
    private static readonly Money Gross = Money.FromExact(8_000.00m);

    private async Task<int> SeedLeadAsync()
    {
        var lead = Lead.Create("M. Klein", "0176 1234567", "m.klein@example.com", LeadSource.Phone);
        await using var context = fixture.CreateContext();
        context.Leads.Add(lead);
        await context.SaveChangesAsync();
        return lead.Id;
    }

    private async Task<int> SeedUserAsync()
    {
        var user = new ApplicationUser { Name = "Test Admin" };
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Invoices.ProjectId is a real FK, so this needs a genuinely persisted chain.</summary>
    private async Task<int> SeedProjectAsync()
    {
        var leadId = await SeedLeadAsync();
        var userId = await SeedUserAsync();

        await using var context = fixture.CreateContext();

        var customer = Customer.Create(leadId, "M. Klein", "m.klein@example.com", "0176 1234567");
        context.Customers.Add(customer);

        var angebot = Angebot.Create(leadId, null, $"ANG-{Guid.NewGuid():N}"[..18], userId);
        context.Angebote.Add(angebot);
        await context.SaveChangesAsync();

        var project = Project.Create(customer.Id, angebot.Id, Money.FromExact(25_673.36m));
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        return project.Id;
    }

    private static Invoice NewInvoice(int projectId) =>
        Invoice.Create(projectId, $"RE-{Guid.NewGuid():N}"[..17], DateTime.UtcNow.AddDays(14), Net, Vat, Gross);

    // ---- Round trip ----------------------------------------------------

    [Fact]
    public async Task AnInvoiceRoundTripsWithEveryField()
    {
        var projectId = await SeedProjectAsync();
        var invoice = NewInvoice(projectId);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Invoices.Add(invoice);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Invoices.SingleAsync(i => i.Id == invoice.Id);

        Assert.Equal(projectId, reloaded.ProjectId);
        Assert.Equal(invoice.InvoiceNumber, reloaded.InvoiceNumber);
        Assert.Equal(InvoiceStatus.Draft, reloaded.Status);
        Assert.Equal(Net, reloaded.NetAmount);
        Assert.Equal(Vat, reloaded.VatAmount);
        Assert.Equal(Gross, reloaded.GrossAmount);
        Assert.Null(reloaded.VoidReason);
    }

    /// <summary>
    /// The Domain's <c>Net + VAT == Gross</c> invariant is only meaningful if all three survive
    /// storage identically. A column type that re-rounded any one of them would break the equality
    /// on the way back out — read through raw SQL so EF's converter cannot mask it. Phase 7 Slice 2
    /// proved this failure mode is real: <c>decimal(18,0)</c> stored 12345.67 as 12346.
    /// </summary>
    [Fact]
    public async Task AllThreeAmountsRoundTripAtFullPrecision()
    {
        var projectId = await SeedProjectAsync();
        var invoice = Invoice.Create(
            projectId, $"RE-{Guid.NewGuid():N}"[..17], DateTime.UtcNow,
            Money.FromExact(10_378.15m), Money.FromExact(1_967.52m), Money.FromExact(12_345.67m));

        await using var context = fixture.CreateContext();
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        var stored = await context.Database
            .SqlQuery<decimal>($"SELECT NetAmount AS Value FROM Invoices WHERE Id = {invoice.Id}")
            .SingleAsync();
        Assert.Equal(10_378.15m, stored);

        stored = await context.Database
            .SqlQuery<decimal>($"SELECT VatAmount AS Value FROM Invoices WHERE Id = {invoice.Id}")
            .SingleAsync();
        Assert.Equal(1_967.52m, stored);

        stored = await context.Database
            .SqlQuery<decimal>($"SELECT GrossAmount AS Value FROM Invoices WHERE Id = {invoice.Id}")
            .SingleAsync();
        Assert.Equal(12_345.67m, stored);
    }

    /// <summary>
    /// Status is stored as its name, not its ordinal (ERD.md's readability reason) — which is also
    /// why reordering <see cref="InvoiceStatus"/> can never silently reinterpret existing rows.
    /// </summary>
    [Fact]
    public async Task StatusIsStoredAsAString()
    {
        var invoice = NewInvoice(await SeedProjectAsync());
        invoice.Send();

        await using var context = fixture.CreateContext();
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        var stored = await context.Database
            .SqlQuery<string>($"SELECT Status AS Value FROM Invoices WHERE Id = {invoice.Id}")
            .SingleAsync();

        Assert.Equal(nameof(InvoiceStatus.Sent), stored);
    }

    // ---- Constraints ---------------------------------------------------

    /// <summary>
    /// BR-9: an invoice number is never reused. The database is the last place that rule can be
    /// enforced, so it must refuse a duplicate outright.
    /// </summary>
    [Fact]
    public async Task TwoInvoicesCannotShareANumber()
    {
        var projectId = await SeedProjectAsync();
        var number = $"RE-{Guid.NewGuid():N}"[..17];

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Invoices.Add(
                Invoice.Create(projectId, number, DateTime.UtcNow, Net, Vat, Gross));
            await writeContext.SaveChangesAsync();
        }

        await using var duplicateContext = fixture.CreateContext();
        duplicateContext.Invoices.Add(
            Invoice.Create(await SeedProjectAsync(), number, DateTime.UtcNow, Net, Vat, Gross));

        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
    }

    /// <summary>
    /// ERD.md §4: "One Project has many Invoices." <c>ProjectId</c> must therefore not be unique —
    /// pinned, because the whole point of FR-8.1 is splitting one agreed total across several
    /// invoices, and a unique index here would make the phase's central feature impossible.
    /// </summary>
    [Fact]
    public async Task OneProjectCanHaveManyInvoices()
    {
        var projectId = await SeedProjectAsync();

        await using var context = fixture.CreateContext();
        context.Invoices.Add(NewInvoice(projectId));
        context.Invoices.Add(NewInvoice(projectId));
        context.Invoices.Add(NewInvoice(projectId));
        await context.SaveChangesAsync();

        Assert.Equal(3, await context.Invoices.CountAsync(i => i.ProjectId == projectId));
    }

    [Fact]
    public async Task AnInvoiceReferencingNoRealProjectIsRejected()
    {
        await using var context = fixture.CreateContext();
        context.Invoices.Add(
            Invoice.Create(999_999_999, $"RE-{Guid.NewGuid():N}"[..17], DateTime.UtcNow, Net, Vat, Gross));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>ERD.md types <c>VoidReason</c> as nullable — only a voided Invoice carries one.</summary>
    [Fact]
    public async Task AVoidedInvoicePersistsItsReasonAndAnUnvoidedOneStoresNull()
    {
        var projectId = await SeedProjectAsync();
        var voided = NewInvoice(projectId);
        voided.Void("Duplicate of the previous invoice.");
        var untouched = NewInvoice(projectId);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Invoices.AddRange(voided, untouched);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();

        Assert.Equal(
            "Duplicate of the previous invoice.",
            (await readContext.Invoices.SingleAsync(i => i.Id == voided.Id)).VoidReason);
        Assert.Null((await readContext.Invoices.SingleAsync(i => i.Id == untouched.Id)).VoidReason);
    }

    /// <summary>
    /// BR-9: a voided Invoice keeps its number, so the sequence has no hole where a row was
    /// destroyed. Voiding must be a status change on a surviving row, never a delete.
    /// </summary>
    [Fact]
    public async Task AVoidedInvoiceKeepsItsRowAndItsNumber()
    {
        var invoice = NewInvoice(await SeedProjectAsync());
        var number = invoice.InvoiceNumber;

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Invoices.Add(invoice);
            await writeContext.SaveChangesAsync();
        }

        await using (var mutateContext = fixture.CreateContext())
        {
            var loaded = await mutateContext.Invoices.SingleAsync(i => i.Id == invoice.Id);
            loaded.Void("Wrong amount.");
            await mutateContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Invoices.SingleAsync(i => i.Id == invoice.Id);

        Assert.Equal(InvoiceStatus.Void, reloaded.Status);
        Assert.Equal(number, reloaded.InvoiceNumber);
    }

    // ---- Payments (the Invoice aggregate's child) -----------------------

    /// <summary>
    /// <b>The question this slice could not answer by inspection.</b> <see cref="Payment"/> is
    /// materialised through an <c>internal</c> constructor whose first parameter is a
    /// <see cref="Money"/> — a type that only reaches the database through
    /// <c>MoneyConverter</c>. EF Core must apply the converter while binding a constructor
    /// parameter, not merely while writing a settable property. <c>AngebotItem</c> has done exactly
    /// this since Phase 3, but "it works elsewhere" is an argument, not a verification.
    /// </summary>
    [Fact]
    public async Task APaymentMaterialisesThroughItsInternalConstructorWithAConvertedMoney()
    {
        var adminId = await SeedUserAsync();
        var invoice = NewInvoice(await SeedProjectAsync());
        invoice.Send();
        var paidAt = new DateTime(2026, 8, 20, 14, 5, 0, DateTimeKind.Utc);
        invoice.MarkPaid(PaymentMethod.BankTransfer, paidAt, adminId);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Invoices.Add(invoice);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Invoices
            .Include(i => i.Payments)
            .SingleAsync(i => i.Id == invoice.Id);

        var payment = Assert.Single(reloaded.Payments);
        Assert.Equal(Gross, payment.Amount);
        Assert.Equal(PaymentMethod.BankTransfer, payment.Method);
        Assert.Equal(paidAt, payment.PaidAt);
        Assert.Equal(adminId, payment.RecordedByAdminId);
        Assert.Equal(InvoiceStatus.Paid, reloaded.Status);
    }

    /// <summary>
    /// <c>Payment.Amount</c> is always a copy of the Invoice's own gross, so the two must be able to
    /// hold the same value exactly — a narrower scale here could make a payment silently disagree
    /// with the invoice it settles.
    /// </summary>
    [Fact]
    public async Task PaymentAmountRoundTripsAtFullPrecision()
    {
        var adminId = await SeedUserAsync();
        var invoice = Invoice.Create(
            await SeedProjectAsync(), $"RE-{Guid.NewGuid():N}"[..17], DateTime.UtcNow,
            Money.FromExact(10_378.15m), Money.FromExact(1_967.52m), Money.FromExact(12_345.67m));
        invoice.Send();
        invoice.MarkPaid(PaymentMethod.Cash, DateTime.UtcNow, adminId);

        await using var context = fixture.CreateContext();
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        var stored = await context.Database
            .SqlQuery<decimal>($"SELECT Amount AS Value FROM Payments WHERE InvoiceId = {invoice.Id}")
            .SingleAsync();

        Assert.Equal(12_345.67m, stored);
    }

    [Fact]
    public async Task PaymentMethodIsStoredAsAString()
    {
        var adminId = await SeedUserAsync();
        var invoice = NewInvoice(await SeedProjectAsync());
        invoice.Send();
        invoice.MarkPaid(PaymentMethod.Other, DateTime.UtcNow, adminId);

        await using var context = fixture.CreateContext();
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        var stored = await context.Database
            .SqlQuery<string>($"SELECT Method AS Value FROM Payments WHERE InvoiceId = {invoice.Id}")
            .SingleAsync();

        Assert.Equal(nameof(PaymentMethod.Other), stored);
    }

    /// <summary>
    /// The shadow <c>InvoiceId</c> FK must be NOT NULL. D46 found this exact column nullable in the
    /// first migration EF generated for a child collection, because <c>IsRequired()</c> had been
    /// omitted — a payment belonging to no invoice is meaningless, and the schema must say so.
    /// </summary>
    [Fact]
    public async Task ThePaymentInvoiceForeignKeyIsNotNullable()
    {
        await using var context = fixture.CreateContext();

        var isNullable = await context.Database
            .SqlQuery<string>($@"
                SELECT IS_NULLABLE AS Value FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Payments' AND COLUMN_NAME = 'InvoiceId'")
            .SingleAsync();

        Assert.Equal("NO", isNullable);
    }

    [Fact]
    public async Task APaymentRecordedByNoRealUserIsRejected()
    {
        var invoice = NewInvoice(await SeedProjectAsync());
        invoice.Send();
        invoice.MarkPaid(PaymentMethod.BankTransfer, DateTime.UtcNow, 999_999_999);

        await using var context = fixture.CreateContext();
        context.Invoices.Add(invoice);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    /// <c>MarkPaid</c> on an entity loaded from the database persists both the new child row and the
    /// status change through <c>SaveChangesAsync</c> alone — there is no <c>UpdateAsync</c> anywhere
    /// in this project, so Slice 5's command will depend entirely on the change tracker seeing both.
    /// </summary>
    [Fact]
    public async Task MarkPaidOnALoadedInvoicePersistsViaSaveChangesAlone()
    {
        var adminId = await SeedUserAsync();
        var invoice = NewInvoice(await SeedProjectAsync());
        invoice.Send();

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Invoices.Add(invoice);
            await writeContext.SaveChangesAsync();
        }

        await using (var mutateContext = fixture.CreateContext())
        {
            var loaded = await mutateContext.Invoices
                .Include(i => i.Payments)
                .SingleAsync(i => i.Id == invoice.Id);

            loaded.MarkPaid(PaymentMethod.BankTransfer, DateTime.UtcNow, adminId);
            await mutateContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Invoices
            .Include(i => i.Payments)
            .SingleAsync(i => i.Id == invoice.Id);

        Assert.Equal(InvoiceStatus.Paid, reloaded.Status);
        Assert.Single(reloaded.Payments);
    }

    /// <summary>
    /// <c>Payment</c> is a child of the Invoice aggregate, so it gets no <c>DbSet</c> of its own
    /// (CLAUDE.md §21) — the persistence layer offers no way to query payments independently of the
    /// root that owns them.
    /// </summary>
    [Fact]
    public void PaymentHasNoDbSetOfItsOwn()
    {
        var dbSetProperties = typeof(RenoTrack.Infrastructure.Persistence.RenoTrackDbContext)
            .GetProperties()
            .Select(p => p.PropertyType)
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(DbSet<>))
            .SelectMany(t => t.GenericTypeArguments);

        Assert.DoesNotContain(typeof(Payment), dbSetProperties);
    }
}
