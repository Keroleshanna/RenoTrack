using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Invoices.Commands.RecordPayment;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Invoices.Commands.RecordPayment;

/// <summary>
/// FR-8.4. The transition and the Payment's shape are the aggregate's own and are proved
/// exhaustively in <c>InvoiceTests</c>; what these prove is that the handler records exactly one
/// Payment for the right amount, commits both changes together, audits after the commit, and sends
/// nothing.
/// </summary>
public class RecordPaymentCommandHandlerTests
{
    private const int AdminId = 2;
    private const int InvoiceId = 55;

    private readonly FakeInvoiceRepository _invoiceRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly RecordPaymentCommandHandler _handler;

    public RecordPaymentCommandHandlerTests()
    {
        _handler = new RecordPaymentCommandHandler(
            new RecordPaymentCommandValidator(),
            _invoiceRepository,
            _unitOfWork,
            _auditService);
    }

    private Invoice SeedInvoice(InvoiceStatus status = InvoiceStatus.Sent)
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
                invoice.Void("Superseded.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unhandled InvoiceStatus.");
        }

        return invoice;
    }

    private static RecordPaymentCommand Command => new(
        InvoiceId, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), PaymentMethod.BankTransfer, AdminId);

    // ---- Happy path -----------------------------------------------------

    [Theory]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Overdue)]
    public async Task ASentOrOverdueInvoiceBecomesPaid(InvoiceStatus from)
    {
        var invoice = SeedInvoice(from);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(InvoiceStatus.Paid, result.Status);
    }

    [Fact]
    public async Task ExactlyOnePaymentIsRecordedWithTheSuppliedDateAndMethod()
    {
        var invoice = SeedInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        var payment = Assert.Single(invoice.Payments);
        Assert.Equal(PaymentMethod.BankTransfer, payment.Method);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), payment.PaidAt);
        Assert.Equal(AdminId, payment.RecordedByAdminId);
    }

    /// <summary>
    /// <b>Phase 8 is full-payment-only.</b> The recorded amount is always the Invoice's own gross —
    /// there is no amount on the command to supply anything else.
    /// </summary>
    [Fact]
    public async Task ThePaymentAmountIsTheInvoicesGross()
    {
        var invoice = SeedInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(invoice.GrossAmount, Assert.Single(invoice.Payments).Amount);
    }

    [Fact]
    public async Task TheStatusChangeAndThePaymentShareOneSaveChanges()
    {
        SeedInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        Assert.Equal(0, _unitOfWork.BeginTransactionCallCount);
    }

    [Fact]
    public async Task ThePaymentIsAuditedAgainstTheInvoice()
    {
        SeedInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        var entry = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(Invoice), entry.EntityType);
        Assert.Equal(InvoiceId, entry.EntityId);
        Assert.Equal(AuditAction.InvoicePaid, entry.Action);
        Assert.Equal(AdminId, entry.PerformedByUserId);
    }

    // ---- Guards ---------------------------------------------------------

    [Fact]
    public async Task AnUnknownInvoiceIsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));
    }

    /// <summary>StateMachine.md §3.3 draws <c>MarkPaid</c> only from <c>Sent</c> and <c>Overdue</c>.</summary>
    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Void)]
    public async Task AnInvoiceInAnyOtherStateIsRejected(InvoiceStatus from)
    {
        SeedInvoice(from);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));
    }

    /// <summary>
    /// <b>A duplicate confirmation is impossible, not merely discouraged.</b> <c>Paid</c> is
    /// terminal, so a second mark-paid is refused by the aggregate and no second Payment row can
    /// ever exist — which is what keeps the one-to-many schema from being mistaken for
    /// partial-payment support.
    /// </summary>
    [Fact]
    public async Task ASecondPaymentIsRefusedAndAddsNoRow()
    {
        var invoice = SeedInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));

        Assert.Single(invoice.Payments);
    }

    [Fact]
    public async Task AnInvalidMethodFailsValidation()
    {
        SeedInvoice();

        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(
                Command with { Method = (PaymentMethod)99 }, CancellationToken.None));
    }

    [Fact]
    public async Task ARejectedPaymentLeavesNoTrace()
    {
        var invoice = SeedInvoice(InvoiceStatus.Draft);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));

        Assert.Empty(invoice.Payments);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    // ---- Structure -------------------------------------------------------

    /// <summary>
    /// `PermissionMatrix.md` §5 marks "Mark Invoice Paid" Admin <c>F</c> — no ownership rule exists
    /// to enforce (CLAUDE.md §16).
    /// </summary>
    [Fact]
    public void TheHandlerTakesNoOwnershipValidator()
    {
        var parameterTypes = typeof(RecordPaymentCommandHandler)
            .GetConstructors().Single().GetParameters().Select(p => p.ParameterType);

        Assert.DoesNotContain(typeof(IOwnershipValidator), parameterTypes);
    }

    /// <summary>
    /// <b>No notification.</b> FR-9.1 covers sending; FR-9.2's three Admin triggers do not include a
    /// payment; Sequence Diagram §9's mark-paid segment draws no mail step. An <c>IEmailSender</c>
    /// dependency appearing here would mean one was invented.
    /// </summary>
    [Fact]
    public void TheHandlerSendsNoEmail()
    {
        var parameterTypes = typeof(RecordPaymentCommandHandler)
            .GetConstructors().Single().GetParameters().Select(p => p.ParameterType);

        Assert.DoesNotContain(typeof(IEmailSender), parameterTypes);
    }

    /// <summary>
    /// No amount reaches this command from anywhere — pinned by signature, so partial payment cannot
    /// arrive by adding a field to a request record.
    /// </summary>
    [Fact]
    public void TheCommandCarriesNoAmount()
    {
        var parameterTypes = typeof(RecordPaymentCommand)
            .GetConstructors().Single().GetParameters().Select(p => p.ParameterType);

        Assert.DoesNotContain(typeof(Money), parameterTypes);
        Assert.DoesNotContain(typeof(decimal), parameterTypes);
    }
}
