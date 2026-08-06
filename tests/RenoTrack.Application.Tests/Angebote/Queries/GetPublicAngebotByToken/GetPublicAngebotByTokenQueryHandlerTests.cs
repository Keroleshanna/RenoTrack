using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Angebote.Queries.GetPublicAngebotByToken;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Queries.GetPublicAngebotByToken;

public class GetPublicAngebotByTokenQueryHandlerTests
{
    private const int OwningInspectorId = 5;
    private const int AdminId = 2;
    private const string Token = "public-token-1";

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly FakeTokenLinkRepository _tokenLinkRepository = new();
    private readonly GetPublicAngebotByTokenQueryHandler _handler;

    public GetPublicAngebotByTokenQueryHandlerTests()
    {
        _handler = new GetPublicAngebotByTokenQueryHandler(
            new GetPublicAngebotByTokenQueryValidator(),
            _tokenLinkRepository,
            _angebotRepository);
    }

    /// <summary>Drives the Angebot to Sent through its own transitions, exactly as production does.</summary>
    private Angebot SeedSentAngebot()
    {
        var angebot = _angebotRepository.Seed(Angebot.Create(leadId: 1, inspectionId: null, "ANG-2026-00042", OwningInspectorId));

        var demolition = angebot.AddSection("Pos. 1 Abriss", 1);
        angebot.AddItemToSection(demolition, "Wände abbrechen", 10m, ItemUnit.SquareMeter(), Money.FromExact(25.00m), VatRate.Standard);

        var setup = angebot.AddSection("Pos. 2 Baustelleneinrichtung", 2);
        angebot.AddItemToSection(setup, "Gerüst", 1m, ItemUnit.LumpSum(), Money.FromExact(100.00m), VatRate.Sixteen);

        angebot.SubmitForReview();
        angebot.Approve(AdminId);
        angebot.Send();

        return angebot;
    }

    private async Task<TokenLink> SeedTokenLinkAsync(
        int entityId,
        TokenLinkEntityType entityType = TokenLinkEntityType.Angebot,
        string token = Token,
        TimeSpan? lifetime = null)
    {
        var link = TokenLink.Create(entityType, entityId, token, DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromDays(30)));
        await _tokenLinkRepository.AddAsync(link, CancellationToken.None);
        return link;
    }

    // ---- Happy path -------------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_ReturnsTheAngebotBehindTheToken()
    {
        var angebot = SeedSentAngebot();
        await SeedTokenLinkAsync(angebot.Id);

        var result = await _handler.HandleAsync(new GetPublicAngebotByTokenQuery(Token), CancellationToken.None);

        Assert.Equal("ANG-2026-00042", result.AngebotNumber);
        Assert.Equal(angebot.NetTotal.Amount, result.NetTotal);
        Assert.Equal(angebot.GrossTotal.Amount, result.GrossTotal);
    }

    [Fact]
    public async Task HandleAsync_ReturnsSectionsInSortOrderWithTheirItems()
    {
        var angebot = SeedSentAngebot();
        await SeedTokenLinkAsync(angebot.Id);

        var result = await _handler.HandleAsync(new GetPublicAngebotByTokenQuery(Token), CancellationToken.None);

        Assert.Collection(
            result.Sections,
            first =>
            {
                Assert.Equal("Pos. 1 Abriss", first.Title);
                Assert.Equal(250.00m, first.Subtotal);
                var item = Assert.Single(first.Items);
                Assert.Equal("Wände abbrechen", item.Description);
                Assert.Equal("m2", item.Unit);
                Assert.Equal(250.00m, item.LineTotal);
            },
            second => Assert.Equal("Pos. 2 Baustelleneinrichtung", second.Title));
    }

    /// <summary>
    /// Wireframe A3's "zzgl. 16% MwSt / zzgl. 19% MwSt" rows — the rate is the percentage the page
    /// prints, not an enum member name.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ExposesVatRatesAsPercentages()
    {
        var angebot = SeedSentAngebot();
        await SeedTokenLinkAsync(angebot.Id);

        var result = await _handler.HandleAsync(new GetPublicAngebotByTokenQuery(Token), CancellationToken.None);

        Assert.Equal([16m, 19m], [.. result.VatBreakdown.Select(line => line.Rate).Order()]);
    }

    // ---- Decision state ---------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_BeforeADecision_ReportsPending()
    {
        var angebot = SeedSentAngebot();
        await SeedTokenLinkAsync(angebot.Id);

        var result = await _handler.HandleAsync(new GetPublicAngebotByTokenQuery(Token), CancellationToken.None);

        Assert.Equal(PublicAngebotDecision.Pending, result.Decision);
        Assert.Null(result.DecisionAt);
    }

    [Fact]
    public async Task HandleAsync_AfterApproval_ReportsApprovedWithATimestamp()
    {
        var angebot = SeedSentAngebot();
        angebot.RecordCustomerApproval();
        await SeedTokenLinkAsync(angebot.Id);

        var result = await _handler.HandleAsync(new GetPublicAngebotByTokenQuery(Token), CancellationToken.None);

        Assert.Equal(PublicAngebotDecision.Approved, result.Decision);
        Assert.NotNull(result.DecisionAt);
    }

    [Fact]
    public async Task HandleAsync_AfterRejection_ReportsRejected()
    {
        var angebot = SeedSentAngebot();
        angebot.RecordCustomerRejection();
        await SeedTokenLinkAsync(angebot.Id);

        var result = await _handler.HandleAsync(new GetPublicAngebotByTokenQuery(Token), CancellationToken.None);

        Assert.Equal(PublicAngebotDecision.Rejected, result.Decision);
    }

    /// <summary>
    /// BR-4 read literally: single use restricts *state-changing* actions, and "viewing (read-only)
    /// remains allowed". A customer who has already approved must still be able to re-read what
    /// they agreed to. This is the test that would fail if the UsedAt check were ever copied from
    /// the decision endpoint into this one.
    /// </summary>
    [Fact]
    public async Task HandleAsync_StillReturnsTheAngebotAfterTheTokenHasBeenUsed()
    {
        var angebot = SeedSentAngebot();
        angebot.RecordCustomerApproval();
        var link = await SeedTokenLinkAsync(angebot.Id);
        link.MarkUsed();

        var result = await _handler.HandleAsync(new GetPublicAngebotByTokenQuery(Token), CancellationToken.None);

        Assert.Equal("ANG-2026-00042", result.AngebotNumber);
    }

    // ---- Sequence Diagram §12 validation ------------------------------------------------------

    [Fact]
    public async Task HandleAsync_ForAnUnknownToken_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new GetPublicAngebotByTokenQuery("no-such-token"), CancellationToken.None));
    }

    /// <summary>
    /// An Invoice token must be indistinguishable from an unknown one: confirming "that token is
    /// real, it just belongs to something else" tells an anonymous caller something they have no
    /// business learning, for no benefit to a legitimate holder.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ForAnInvoiceToken_ThrowsTheSameNotFoundAsAnUnknownToken()
    {
        var angebot = SeedSentAngebot();
        await SeedTokenLinkAsync(angebot.Id, TokenLinkEntityType.Invoice);

        var wrongType = await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new GetPublicAngebotByTokenQuery(Token), CancellationToken.None));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new GetPublicAngebotByTokenQuery("no-such-token"), CancellationToken.None));

        Assert.Equal(unknown.Message, wrongType.Message);
    }

    [Fact]
    public async Task HandleAsync_ForAnExpiredToken_ThrowsGone()
    {
        var angebot = SeedSentAngebot();
        await SeedTokenLinkAsync(angebot.Id, lifetime: TimeSpan.FromMilliseconds(50));
        Thread.Sleep(120);

        await Assert.ThrowsAsync<GoneException>(
            () => _handler.HandleAsync(new GetPublicAngebotByTokenQuery(Token), CancellationToken.None));
    }

    /// <summary>The token must never be echoed in a message that becomes ProblemDetails and a log entry.</summary>
    [Fact]
    public async Task HandleAsync_NeverPutsTheTokenInTheFailureMessage()
    {
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new GetPublicAngebotByTokenQuery("a-secret-token-value"), CancellationToken.None));

        Assert.DoesNotContain("a-secret-token-value", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WithABlankToken_ThrowsValidationException(string token)
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new GetPublicAngebotByTokenQuery(token), CancellationToken.None));
    }
}
