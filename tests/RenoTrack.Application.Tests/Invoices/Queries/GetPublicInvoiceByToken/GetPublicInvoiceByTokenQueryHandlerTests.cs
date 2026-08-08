using FluentValidation;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Invoices.Dtos;
using RenoTrack.Application.Invoices.Queries.GetPublicInvoiceByToken;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Invoices.Queries.GetPublicInvoiceByToken;

/// <summary>
/// The second unauthenticated read in the system. These prove Sequence Diagram §12's checks are
/// applied — and, just as importantly, that the one check §12 scopes to "decision-type actions
/// only" is <b>not</b> applied here, since no Invoice decision action exists.
/// </summary>
public class GetPublicInvoiceByTokenQueryHandlerTests
{
    private const int InvoiceId = 55;
    private const int ProjectId = 77;

    private readonly FakeTokenLinkRepository _tokenLinkRepository = new();
    private readonly FakeInvoiceRepository _invoiceRepository = new();
    private readonly FakeProjectRepository _projectRepository = new();
    private readonly FakeCustomerRepository _customerRepository = new();
    private readonly GetPublicInvoiceByTokenQueryHandler _handler;

    public GetPublicInvoiceByTokenQueryHandlerTests()
    {
        _handler = new GetPublicInvoiceByTokenQueryHandler(
            new GetPublicInvoiceByTokenQueryValidator(),
            _tokenLinkRepository,
            _invoiceRepository,
            _projectRepository,
            _customerRepository);
    }

    private Invoice SeedSentInvoice()
    {
        var customer = _customerRepository.Seed(
            Customer.Create(leadId: 1, "M. Klein", "m.klein@example.com", "0176 1234567"));

        _projectRepository.Seed(
            Project.Create(customer.Id, angebotId: 3, Money.FromExact(25_673.36m)), ProjectId);

        var invoice = _invoiceRepository.Seed(
            Invoice.Create(
                ProjectId, "RE-2026-00017", new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc),
                Money.FromExact(6_722.69m), Money.FromExact(1_277.31m), Money.FromExact(8_000.00m)),
            InvoiceId);

        invoice.Send();
        return invoice;
    }

    private async Task<string> SeedTokenAsync(
        TokenLinkEntityType entityType = TokenLinkEntityType.Invoice,
        int? entityId = null,
        DateTime? expiresAt = null)
    {
        var link = TokenLink.Create(
            entityType,
            entityId ?? InvoiceId,
            $"token-{Guid.NewGuid():N}",
            expiresAt ?? DateTime.UtcNow.AddDays(30));

        await _tokenLinkRepository.AddAsync(link, CancellationToken.None);
        return link.Token;
    }

    // ---- Happy path -----------------------------------------------------

    [Fact]
    public async Task AValidTokenReturnsTheInvoice()
    {
        SeedSentInvoice();
        var token = await SeedTokenAsync();

        var result = await _handler.HandleAsync(new GetPublicInvoiceByTokenQuery(token), CancellationToken.None);

        Assert.Equal("RE-2026-00017", result.InvoiceNumber);
        Assert.Equal("M. Klein", result.CustomerName);
        Assert.Equal(6_722.69m, result.NetAmount);
        Assert.Equal(1_277.31m, result.VatAmount);
        Assert.Equal(8_000.00m, result.GrossAmount);
        Assert.Equal(new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc), result.DueDate);
    }

    // ---- Sequence Diagram §12 -------------------------------------------

    [Fact]
    public async Task AnUnknownTokenIsNotFound()
    {
        SeedSentInvoice();

        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new GetPublicInvoiceByTokenQuery("nope"), CancellationToken.None));
    }

    /// <summary>
    /// An Angebot token must be indistinguishable from an unknown one — confirming "that token is
    /// real, but it belongs to something else" leaks the token's existence for no benefit to anyone
    /// legitimately holding an Invoice link.
    /// </summary>
    [Fact]
    public async Task AnAngebotTokenIsNotFoundRatherThanADistinctError()
    {
        SeedSentInvoice();
        var angebotToken = await SeedTokenAsync(TokenLinkEntityType.Angebot);

        var wrongType = await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new GetPublicInvoiceByTokenQuery(angebotToken), CancellationToken.None));

        var unknown = await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new GetPublicInvoiceByTokenQuery("nope"), CancellationToken.None));

        Assert.Equal(unknown.Message, wrongType.Message);
    }

    [Fact]
    public async Task AnExpiredTokenIsGone()
    {
        SeedSentInvoice();

        // Created valid, then expired by moving past its own window — TokenLink.Create refuses a
        // link born expired, so the lifetime is shortened rather than backdated.
        var token = await SeedTokenAsync(expiresAt: DateTime.UtcNow.AddMilliseconds(50));
        Thread.Sleep(120);

        await Assert.ThrowsAsync<GoneException>(
            () => _handler.HandleAsync(new GetPublicInvoiceByTokenQuery(token), CancellationToken.None));
    }

    /// <summary>
    /// <b>Prior use is deliberately not checked.</b> §12 scopes the <c>UsedAt</c> check to
    /// "decision-type actions only", and `PermissionMatrix.md` §7 grants the customer nothing but
    /// viewing for an Invoice — so no Invoice decision action exists and no link is ever consumed.
    /// A used link must still render, exactly as BR-4 requires for the Angebot read.
    /// </summary>
    [Fact]
    public async Task AUsedTokenStillRendersTheInvoice()
    {
        SeedSentInvoice();
        var token = await SeedTokenAsync();
        _tokenLinkRepository.AddedTokenLinks.Single().MarkUsed();

        var result = await _handler.HandleAsync(new GetPublicInvoiceByTokenQuery(token), CancellationToken.None);

        Assert.Equal("RE-2026-00017", result.InvoiceNumber);
    }

    [Fact]
    public async Task AnEmptyTokenFailsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new GetPublicInvoiceByTokenQuery(""), CancellationToken.None));
    }

    [Fact]
    public async Task ATokenPointingAtNoRealInvoiceIsNotFound()
    {
        SeedSentInvoice();
        var token = await SeedTokenAsync(entityId: 999_999);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new GetPublicInvoiceByTokenQuery(token), CancellationToken.None));
    }

    // ---- The public contract --------------------------------------------

    /// <summary>
    /// The public DTO is a separate hierarchy, and what it withholds is the point: no internal ids,
    /// no issue date, no status, no void reason, no payments. Pinned by property name so a field
    /// added for the Dashboard cannot appear on the one endpoint any holder of a forwarded email
    /// can reach.
    /// </summary>
    [Fact]
    public void ThePublicDtoExposesOnlyWhatWireframeA4Renders()
    {
        var properties = typeof(PublicInvoiceDto)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(PublicInvoiceDto.CustomerName),
                nameof(PublicInvoiceDto.DueDate),
                nameof(PublicInvoiceDto.GrossAmount),
                nameof(PublicInvoiceDto.InvoiceNumber),
                nameof(PublicInvoiceDto.NetAmount),
                nameof(PublicInvoiceDto.VatAmount),
            ],
            properties);
    }
}
