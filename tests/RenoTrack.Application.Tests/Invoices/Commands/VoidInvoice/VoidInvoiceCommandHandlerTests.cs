using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Invoices.Commands.VoidInvoice;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Invoices.Commands.VoidInvoice;

/// <summary>
/// PermissionMatrix.md §5 and StateMachine.md §3.3. The transition and the mandatory reason are the
/// aggregate's own; what these prove is that the handler voids without deleting, records the reason
/// in both places the documents require, and audits after the commit.
/// </summary>
public class VoidInvoiceCommandHandlerTests
{
    private const int AdminId = 2;
    private const int InvoiceId = 55;
    private const string Reason = "Issued against the wrong Project.";

    private readonly FakeInvoiceRepository _invoiceRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly VoidInvoiceCommandHandler _handler;

    public VoidInvoiceCommandHandlerTests()
    {
        _handler = new VoidInvoiceCommandHandler(
            new VoidInvoiceCommandValidator(),
            _invoiceRepository,
            _unitOfWork,
            _auditService);
    }

    private Invoice SeedInvoice(InvoiceStatus status = InvoiceStatus.Draft)
    {
        var invoice = _invoiceRepository.Seed(
            Invoice.Create(
                projectId: 77, "RE-2026-00017", DateTime.UtcNow.AddDays(14),
                Money.FromExact(6_722.69m), Money.FromExact(1_277.31m), Money.FromExact(8_000.00m)),
            InvoiceId);

        switch (status)
        {
            case InvoiceStatus.Draft:
                break;
            case InvoiceStatus.Sent:
                invoice.Send();
                break;
            case InvoiceStatus.Overdue:
                invoice.Send();
                invoice.MarkOverdue(invoice.DueDate.AddDays(1));
                break;
            case InvoiceStatus.Paid:
                invoice.Send();
                invoice.MarkPaid(PaymentMethod.Cash, DateTime.UtcNow, AdminId);
                break;
            case InvoiceStatus.Void:
                invoice.Void("Already voided.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unhandled InvoiceStatus.");
        }

        return invoice;
    }

    private static VoidInvoiceCommand Command => new(InvoiceId, Reason, AdminId);

    // ---- Happy path -----------------------------------------------------

    /// <summary>§3.3 permits <c>Draft</c>, <c>Sent</c> and <c>Overdue</c> to be voided.</summary>
    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Overdue)]
    public async Task AVoidableInvoiceBecomesVoid(InvoiceStatus from)
    {
        var invoice = SeedInvoice(from);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(InvoiceStatus.Void, invoice.Status);
        Assert.Equal(InvoiceStatus.Void, result.Status);
        Assert.Equal(Reason, result.VoidReason);
    }

    /// <summary>
    /// BR-9: "An Invoice number, once issued, is never reused or reassigned — even if that Invoice is
    /// later Voided." Nothing deletes the row and nothing can change the number.
    /// </summary>
    [Fact]
    public async Task TheInvoiceKeepsItsNumberAndItsRow()
    {
        var invoice = SeedInvoice(InvoiceStatus.Sent);

        await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal("RE-2026-00017", invoice.InvoiceNumber);
        Assert.NotNull(await _invoiceRepository.GetByIdAsync(InvoiceId, CancellationToken.None));
    }

    [Fact]
    public async Task TheVoidIsPersistedThroughOneSaveChangesWithNoTransaction()
    {
        SeedInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        Assert.Equal(0, _unitOfWork.BeginTransactionCallCount);
    }

    /// <summary>
    /// StateMachine.md §3.3 requires an "AuditLog entry **with reason**" — so the reason appears in
    /// <c>details</c> as well as on the invoice row itself.
    /// </summary>
    [Fact]
    public async Task TheVoidIsAuditedAgainstTheInvoiceWithTheReason()
    {
        SeedInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        var entry = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(Invoice), entry.EntityType);
        Assert.Equal(InvoiceId, entry.EntityId);
        Assert.Equal(AuditAction.InvoiceVoided, entry.Action);
        Assert.Equal(AdminId, entry.PerformedByUserId);
        Assert.Contains(Reason, entry.Details);
    }

    // ---- Guards ---------------------------------------------------------

    [Fact]
    public async Task AnUnknownInvoiceIsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));
    }

    /// <summary>§3.2 gives <c>Paid</c> and <c>Void</c> no outgoing edge at all.</summary>
    [Theory]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Void)]
    public async Task ATerminalInvoiceCannotBeVoided(InvoiceStatus from)
    {
        SeedInvoice(from);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));
    }

    /// <summary>
    /// `PermissionMatrix.md` §5 requires a reason without qualification — including from
    /// <c>Draft</c>, which is the reconciliation Slice 1 made to §3.3's blank guard cell.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankReasonFailsValidation(string reason)
    {
        SeedInvoice();

        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(Command with { Reason = reason }, CancellationToken.None));
    }

    [Fact]
    public async Task ARejectedVoidLeavesNoTrace()
    {
        var invoice = SeedInvoice(InvoiceStatus.Paid);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));

        Assert.Null(invoice.VoidReason);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    // ---- Structure -------------------------------------------------------

    [Fact]
    public void TheHandlerTakesNoOwnershipValidator()
    {
        var parameterTypes = typeof(VoidInvoiceCommandHandler)
            .GetConstructors().Single().GetParameters().Select(p => p.ParameterType);

        Assert.DoesNotContain(typeof(IOwnershipValidator), parameterTypes);
    }

    /// <summary>No document describes a notification for voiding, so none is sent.</summary>
    [Fact]
    public void TheHandlerSendsNoEmail()
    {
        var parameterTypes = typeof(VoidInvoiceCommandHandler)
            .GetConstructors().Single().GetParameters().Select(p => p.ParameterType);

        Assert.DoesNotContain(typeof(IEmailSender), parameterTypes);
    }
}
