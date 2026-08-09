using System.Reflection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Tests.Entities;

public class PaymentTests
{
    /// <summary>
    /// Enforces the aggregate boundary at runtime rather than by convention: Payment must have no
    /// public constructor, so the only way to create one from outside RenoTrack.Domain is through
    /// <c>Invoice.MarkPaid</c>. Re-opening direct creation fails this test immediately — the same
    /// guard <c>InspectionPhoto</c> and <c>AngebotSection</c> carry.
    /// </summary>
    [Fact]
    public void HasNoPublicConstructor()
    {
        var publicConstructors = typeof(Payment).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void ExposesNoPublicSetters()
    {
        var settableProperties = typeof(Payment)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .ToArray();

        Assert.Empty(settableProperties);
    }

    /// <summary>
    /// A Payment is inert data — it records what happened and has no transition of its own.
    /// StateMachine.md models no Payment state machine, so any public method appearing here later
    /// would be a state machine nobody designed.
    /// </summary>
    [Fact]
    public void ExposesNoMutatingMethods()
    {
        var publicMethods = typeof(Payment)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToArray();

        Assert.Empty(publicMethods);
    }

    /// <summary>
    /// CLAUDE.md §2: staff are referenced by id, never as a navigation property. The real
    /// <c>AspNetUsers</c> foreign key is Infrastructure's concern.
    /// </summary>
    [Fact]
    public void ReferencesTheRecordingAdminByIdOnly()
    {
        var recordedBy = typeof(Payment).GetProperty(nameof(Payment.RecordedByAdminId))!;

        Assert.Equal(typeof(int), recordedBy.PropertyType);
    }

    // The remaining behaviour is only reachable through the aggregate root, which is the point —
    // these exercise Payment through Invoice.MarkPaid rather than constructing one directly.

    private static Invoice SentInvoice()
    {
        var invoice = Invoice.Create(
            projectId: 11,
            invoiceNumber: "RE-2026-00017",
            dueDate: DateTime.UtcNow,
            netAmount: Money.FromExact(6_722.69m),
            vatAmount: Money.FromExact(1_277.31m),
            grossAmount: Money.FromExact(8_000.00m));

        invoice.Send();
        return invoice;
    }

    [Fact]
    public void CreatedThroughMarkPaid_CarriesTheSuppliedMethodAndDate()
    {
        var paidAt = new DateTime(2026, 8, 20, 14, 5, 0, DateTimeKind.Utc);

        var payment = SentInvoice().MarkPaid(PaymentMethod.Other, paidAt, recordedByAdminId: 3);

        Assert.Equal(PaymentMethod.Other, payment.Method);
        Assert.Equal(paidAt, payment.PaidAt);
        Assert.Equal(3, payment.RecordedByAdminId);
    }

    /// <summary>
    /// SRS FR-8.4 names exactly Bank Transfer, Cash and Other, and ERD.md's <c>Payments.Method</c>
    /// column repeats them. No gateway value exists yet — FR-8.5 describes what adding one will
    /// cost later, which is not licence to declare an unreachable value now.
    /// </summary>
    [Fact]
    public void PaymentMethod_HasExactlyTheThreeDocumentedValues()
    {
        Assert.Equal(
            [nameof(PaymentMethod.BankTransfer), nameof(PaymentMethod.Cash), nameof(PaymentMethod.Other)],
            Enum.GetNames<PaymentMethod>().OrderBy(n => n).ToArray());
    }

    [Theory]
    [InlineData(PaymentMethod.BankTransfer)]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.Other)]
    public void EveryDocumentedMethodIsAccepted(PaymentMethod method)
    {
        var payment = SentInvoice().MarkPaid(method, DateTime.UtcNow, recordedByAdminId: 3);

        Assert.Equal(method, payment.Method);
    }
}
