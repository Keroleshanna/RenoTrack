using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Invoices.Commands.SendInvoice;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Invoices.Commands.SendInvoice;

/// <summary>
/// Orchestration and ordering. The <c>Draft → Sent</c> transition and its <c>GrossAmount &gt; 0</c>
/// guard are the aggregate's own and are proved exhaustively in <c>InvoiceTests</c>; what these
/// prove is that the handler issues exactly one token, commits both writes together, audits and
/// emails only after that commit, and produces no token at all when the Domain refuses.
/// </summary>
public class SendInvoiceCommandHandlerTests
{
    private const int AdminId = 2;
    private const int InvoiceId = 55;
    private const int ProjectId = 77;
    private const int CustomerId = 9;

    private readonly FakeInvoiceRepository _invoiceRepository = new();
    private readonly FakeProjectRepository _projectRepository = new();
    private readonly FakeCustomerRepository _customerRepository = new();
    private readonly FakeTokenLinkRepository _tokenLinkRepository = new();
    private readonly FakeTokenLinkService _tokenLinkService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly FakeEmailSender _emailSender = new();
    private readonly SendInvoiceCommandHandler _handler;

    public SendInvoiceCommandHandlerTests()
    {
        _handler = new SendInvoiceCommandHandler(
            new SendInvoiceCommandValidator(),
            _invoiceRepository,
            _projectRepository,
            _customerRepository,
            _tokenLinkRepository,
            _tokenLinkService,
            _unitOfWork,
            _auditService,
            _emailSender);
    }

    private Invoice SeedDraftInvoice(decimal gross = 8_000.00m)
    {
        var customer = _customerRepository.Seed(
            Customer.Create(leadId: 1, "M. Klein", "m.klein@example.com", "0176 1234567"));

        _projectRepository.Seed(
            Project.Create(customer.Id, angebotId: 3, Money.FromExact(25_673.36m)), ProjectId);

        var net = Money.RoundedPerBR11(gross / 1.19m);
        return _invoiceRepository.Seed(
            Invoice.Create(
                ProjectId, "RE-2026-00017", DateTime.UtcNow.AddDays(14),
                net, Money.FromExact(gross) - net, Money.FromExact(gross)),
            InvoiceId);
    }

    private static SendInvoiceCommand Command => new(InvoiceId, AdminId);

    // ---- Happy path -----------------------------------------------------

    [Fact]
    public async Task ADraftInvoiceMovesToSent()
    {
        var invoice = SeedDraftInvoice();

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Equal(InvoiceStatus.Sent, result.Status);
    }

    [Fact]
    public async Task ATokenLinkIsIssuedForTheInvoice()
    {
        var invoice = SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        var tokenLink = Assert.Single(_tokenLinkRepository.AddedTokenLinks);
        Assert.Equal(TokenLinkEntityType.Invoice, tokenLink.EntityType);
        Assert.Equal(invoice.Id, tokenLink.EntityId);
        Assert.Null(tokenLink.UsedAt);
    }

    /// <summary>
    /// The status change and the token row must reach the database together: a committed link for an
    /// Invoice that never became <c>Sent</c> is a live credential for a bill nobody issued, and a
    /// <c>Sent</c> Invoice with no link is a customer who cannot see what they owe.
    /// </summary>
    [Fact]
    public async Task BothWritesShareOneSaveChanges()
    {
        SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task NoExplicitTransactionIsOpened()
    {
        SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(0, _unitOfWork.BeginTransactionCallCount);
    }

    [Fact]
    public async Task TheSendIsAuditedAgainstTheInvoice()
    {
        SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        var entry = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(Invoice), entry.EntityType);
        Assert.Equal(InvoiceId, entry.EntityId);
        Assert.Equal(AuditAction.InvoiceSent, entry.Action);
        Assert.Equal(AdminId, entry.PerformedByUserId);
    }

    /// <summary>
    /// Sequence Diagram §9 addresses the mail to <c>Customer.Email</c>, reached through
    /// <c>Invoice → Project → Customer</c>. The notification carries the raw token, never a URL —
    /// the base address is deployment configuration Application deliberately cannot see.
    /// </summary>
    [Fact]
    public async Task TheCustomerIsEmailedTheTokenLink()
    {
        var invoice = SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        var notification = Assert.Single(_emailSender.InvoiceReadyNotifications);
        Assert.Equal("m.klein@example.com", notification.RecipientEmail);
        Assert.Equal("M. Klein", notification.RecipientName);
        Assert.Equal(invoice.InvoiceNumber, notification.InvoiceNumber);
        Assert.Equal(invoice.GrossAmount.Amount, notification.GrossAmount);
        Assert.Equal(_tokenLinkRepository.AddedTokenLinks.Single().Token, notification.Token);
    }

    // ---- Guards ---------------------------------------------------------

    [Fact]
    public async Task AnUnknownInvoiceIsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));
    }

    /// <summary>StateMachine.md §3.3: only a <c>Draft</c> Invoice may be sent.</summary>
    [Fact]
    public async Task AnAlreadySentInvoiceIsRejected()
    {
        SeedDraftInvoice().Send();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));
    }

    /// <summary>StateMachine.md §3.3's guard: "Invoice has a valid GrossAmount &gt; 0".</summary>
    [Fact]
    public async Task AZeroGrossInvoiceCannotBeSent()
    {
        var customer = _customerRepository.Seed(
            Customer.Create(leadId: 1, "M. Klein", "m.klein@example.com", "0176 1234567"));
        _projectRepository.Seed(Project.Create(customer.Id, 3, Money.Zero), ProjectId);
        _invoiceRepository.Seed(
            Invoice.Create(ProjectId, "RE-2026-00018", DateTime.UtcNow, Money.Zero, Money.Zero, Money.Zero),
            InvoiceId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));
    }

    [Fact]
    public async Task ANonPositiveIdFailsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new SendInvoiceCommand(0, AdminId), CancellationToken.None));
    }

    // ---- No residue on rejection ----------------------------------------

    /// <summary>
    /// Architecture §9's ordering principle: the Domain guard runs before a token is generated, so a
    /// refused send issues no credential, commits nothing, audits nothing and emails nothing.
    /// </summary>
    [Fact]
    public async Task ARefusedSendIssuesNoTokenAndLeavesNoTrace()
    {
        SeedDraftInvoice().Send();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));

        Assert.Empty(_tokenLinkRepository.AddedTokenLinks);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
        Assert.Empty(_emailSender.InvoiceReadyNotifications);
    }

    [Fact]
    public async Task AZeroGrossRejectionIssuesNoToken()
    {
        var customer = _customerRepository.Seed(
            Customer.Create(leadId: 1, "M. Klein", "m.klein@example.com", "0176 1234567"));
        _projectRepository.Seed(Project.Create(customer.Id, 3, Money.Zero), ProjectId);
        _invoiceRepository.Seed(
            Invoice.Create(ProjectId, "RE-2026-00018", DateTime.UtcNow, Money.Zero, Money.Zero, Money.Zero),
            InvoiceId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));

        Assert.Empty(_tokenLinkRepository.AddedTokenLinks);
        Assert.Empty(_emailSender.InvoiceReadyNotifications);
    }

    // ---- Structure -------------------------------------------------------

    /// <summary>
    /// `PermissionMatrix.md` §5 marks "Send Invoice" Admin <c>F</c>, so no ownership rule exists to
    /// enforce (CLAUDE.md §16).
    /// </summary>
    [Fact]
    public void TheHandlerTakesNoOwnershipValidator()
    {
        var parameterTypes = typeof(SendInvoiceCommandHandler)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType);

        Assert.DoesNotContain(typeof(IOwnershipValidator), parameterTypes);
    }
}
