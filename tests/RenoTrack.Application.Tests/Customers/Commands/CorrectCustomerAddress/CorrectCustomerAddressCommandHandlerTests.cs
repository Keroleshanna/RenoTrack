using System.Reflection;
using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Documents;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Customers.Commands.CorrectCustomerAddress;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Customers.Commands.CorrectCustomerAddress;

/// <summary>
/// Phase 14 Slice 2b (D112): an Admin sets or corrects the address BR-5 prints on every invoice.
/// The address rules themselves are the aggregate's and are exhaustive in <c>CustomerTests</c>;
/// these prove the handler's orchestration and — the slice's whole purpose — that a corrected
/// Customer is one the invoice document no longer refuses.
/// </summary>
public class CorrectCustomerAddressCommandHandlerTests
{
    private const int AdminId = 2;
    private const string Corrected = "Musterstraße 1\n12345 Musterstadt";

    private readonly FakeCustomerRepository _customerRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly CorrectCustomerAddressCommandHandler _handler;

    public CorrectCustomerAddressCommandHandlerTests()
    {
        _handler = new CorrectCustomerAddressCommandHandler(
            new CorrectCustomerAddressCommandValidator(),
            _customerRepository,
            _unitOfWork,
            _auditService);
    }

    /// <summary>The shape every website-sourced conversion produces: no address at all.</summary>
    private Customer SeedCustomerWithoutAddress() =>
        _customerRepository.Seed(
            Customer.Create(leadId: 11, "Erika Mustermann", "erika@example.test", "+49 000 1234567"));

    [Fact]
    public async Task CorrectsTheAddressSavesOnceAndReturnsTheNarrowDto()
    {
        var customer = SeedCustomerWithoutAddress();

        var dto = await _handler.HandleAsync(
            new CorrectCustomerAddressCommand(customer.Id, "  " + Corrected + "  ", AdminId),
            CancellationToken.None);

        Assert.Equal(Corrected, customer.Address);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        Assert.Equal(customer.Id, dto.Id);
        Assert.Equal(11, dto.LeadId);
        Assert.Equal("Erika Mustermann", dto.Name);
        Assert.Equal(Corrected, dto.Address);
    }

    /// <summary>
    /// H2: audited against the Customer, by the acting Admin, with no before/after address — the
    /// address is personal data and AuditLog is not a field-level change log.
    /// </summary>
    [Fact]
    public async Task AuditsTheCorrectionAgainstTheCustomerWithNoAddressInTheDetails()
    {
        var customer = SeedCustomerWithoutAddress();

        await _handler.HandleAsync(
            new CorrectCustomerAddressCommand(customer.Id, Corrected, AdminId),
            CancellationToken.None);

        var call = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(Customer), call.EntityType);
        Assert.Equal(customer.Id, call.EntityId);
        Assert.Equal(AuditAction.CustomerAddressCorrected, call.Action);
        Assert.Equal(AdminId, call.PerformedByUserId);
        Assert.Null(call.Details);
    }

    /// <summary>The audit follows a successful save, never precedes it (CLAUDE.md §6).</summary>
    [Fact]
    public async Task WritesNoAuditWhenTheSaveFails()
    {
        var customer = SeedCustomerWithoutAddress();
        _unitOfWork.SaveFailure = new InvalidOperationException("database unavailable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(
            new CorrectCustomerAddressCommand(customer.Id, Corrected, AdminId),
            CancellationToken.None));

        Assert.Empty(_auditService.Calls);
    }

    [Fact]
    public async Task AnUnknownCustomerIsNotFoundAndNothingIsSavedOrAudited()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            new CorrectCustomerAddressCommand(CustomerId: 999, Corrected, AdminId),
            CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    /// <summary>H4: a blank address is refused before the aggregate is even loaded.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankAddressIsAValidationFailureAndNothingChanges(string address)
    {
        var customer = SeedCustomerWithoutAddress();

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            new CorrectCustomerAddressCommand(customer.Id, address, AdminId),
            CancellationToken.None));

        Assert.Null(customer.Address);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    /// <summary>
    /// The slice's purpose, proved end to end at the layer where both halves meet: the document
    /// factory refuses an invoice whose customer has no address (D111), and the very same invoice
    /// renders once that Customer's address has been corrected — with no change to the Invoice.
    /// </summary>
    [Fact]
    public async Task ACorrectedCustomerIsOneTheInvoiceDocumentNoLongerRefuses()
    {
        var customer = SeedCustomerWithoutAddress();
        var calendar = InvoiceCalendar.ForEuropeBerlin();
        var invoice = Invoice.Create(
            projectId: 7,
            invoiceNumber: "RE-2026-00017",
            issuedAt: new DateTime(2026, 10, 1, 8, 15, 0, DateTimeKind.Utc),
            dueDate: new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc),
            grossAmount: Money.FromExact(1_190.00m),
            rateMix: [new VatBreakdownLine(VatRate.Standard, Money.FromExact(1_000.00m), Money.FromExact(190.00m))],
            description: "Malerarbeiten Erdgeschoss",
            servicePeriodStart: null,
            servicePeriodEnd: null);

        var refusal = Assert.Throws<InvalidOperationException>(
            () => InvoiceDocumentFactory.Create(invoice, customer, calendar));
        Assert.Contains("the customer's address", refusal.Message);

        await _handler.HandleAsync(
            new CorrectCustomerAddressCommand(customer.Id, Corrected, AdminId),
            CancellationToken.None);

        var document = InvoiceDocumentFactory.Create(invoice, customer, calendar);
        Assert.Equal(Corrected, document.Customer.Address);
        Assert.Equal("Erika Mustermann", document.Customer.Name);
    }

    /// <summary>
    /// The command can express the address and nothing else a caller could change: name, email,
    /// phone and the Lead link are not correctable (D112), so they cannot be named here at all.
    /// </summary>
    [Fact]
    public void TheCommandCarriesOnlyTheCustomerTheAddressAndTheActingAdmin()
    {
        var properties = typeof(CorrectCustomerAddressCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Order()
            .ToArray();

        Assert.Equal(
            ["Address", "CorrectedByAdminId", "CustomerId"],
            properties);
    }
}
