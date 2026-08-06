using FluentValidation;
using RenoTrack.Application.Angebote.Commands.RecordAngebotDecision;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Commands.RecordAngebotDecision;

public class RecordAngebotDecisionCommandHandlerTests
{
    private const int OwningInspectorId = 5;
    private const int AdminId = 2;
    private const string Token = "decision-token-1";

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly FakeLeadRepository _leadRepository = new();
    private readonly FakeTokenLinkRepository _tokenLinkRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly FakeEmailSender _emailSender = new();
    private readonly RecordAngebotDecisionCommandHandler _handler;

    public RecordAngebotDecisionCommandHandlerTests()
    {
        _handler = new RecordAngebotDecisionCommandHandler(
            new RecordAngebotDecisionCommandValidator(),
            _tokenLinkRepository,
            _angebotRepository,
            _leadRepository,
            _unitOfWork,
            _auditService,
            _emailSender);
    }

    /// <summary>
    /// Drives both aggregates to the state a real send leaves them in, entirely through their own
    /// transitions — the Lead reaches <c>AngebotSent</c> only via <c>MarkAngebotSent()</c>, which is
    /// what makes <c>MarkWon()</c>/<c>MarkLost()</c>'s guard meaningful here rather than bypassed.
    /// </summary>
    private async Task<(Angebot Angebot, Lead Lead, TokenLink Link)> SeedSentAngebotAsync(string token = Token)
    {
        var lead = _leadRepository.Seed(Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Website));
        lead.MarkInspectionScheduled();
        lead.MarkInspectionDone();
        lead.MarkAngebotInProgress();

        var angebot = _angebotRepository.Seed(Angebot.Create(lead.Id, null, "ANG-2026-00042", OwningInspectorId));
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(100.00m), VatRate.Standard);
        angebot.SubmitForReview();
        angebot.Approve(AdminId);
        angebot.Send();
        lead.MarkAngebotSent();

        var link = TokenLink.Create(TokenLinkEntityType.Angebot, angebot.Id, token, DateTime.UtcNow.AddDays(30));
        await _tokenLinkRepository.AddAsync(link, CancellationToken.None);

        return (angebot, lead, link);
    }

    // ---- Approval ---------------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Approval_TransitionsTheAngebotAndTheLeadAndConsumesTheLink()
    {
        var (angebot, lead, link) = await SeedSentAngebotAsync();

        var result = await _handler.HandleAsync(
            new RecordAngebotDecisionCommand(Token, CustomerDecision.Approve), CancellationToken.None);

        Assert.Equal(AngebotStatus.CustomerApproved, angebot.Status);
        Assert.Equal(LeadStatus.Won, lead.Status);
        Assert.NotNull(link.UsedAt);
        Assert.NotNull(angebot.DecisionAt);
        Assert.Equal(PublicAngebotDecision.Approved, result.Decision);
    }

    [Fact]
    public async Task HandleAsync_Rejection_TransitionsTheAngebotAndTheLead()
    {
        var (angebot, lead, _) = await SeedSentAngebotAsync();

        var result = await _handler.HandleAsync(
            new RecordAngebotDecisionCommand(Token, CustomerDecision.Reject), CancellationToken.None);

        Assert.Equal(AngebotStatus.CustomerRejected, angebot.Status);
        Assert.Equal(LeadStatus.Lost, lead.Status);
        Assert.Equal(PublicAngebotDecision.Rejected, result.Decision);
    }

    /// <summary>
    /// StateMachine.md §5 states this as an invariant: the Lead reaches <c>Won</c> only inside this
    /// handler's transaction. A second commit would allow a consumed link whose decision never
    /// landed — locking the customer out of answering at all — or an approved Angebot whose Lead
    /// never moved.
    /// </summary>
    [Fact]
    public async Task HandleAsync_CommitsAllThreeAggregatesInOneSaveChanges()
    {
        await SeedSentAngebotAsync();

        await _handler.HandleAsync(
            new RecordAngebotDecisionCommand(Token, CustomerDecision.Approve), CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    // ---- Audit and notification ---------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Approval_AuditsAgainstTheLeadWithNoUserId()
    {
        var (_, lead, _) = await SeedSentAngebotAsync();

        await _handler.HandleAsync(
            new RecordAngebotDecisionCommand(Token, CustomerDecision.Approve), CancellationToken.None);

        var entry = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(Lead), entry.EntityType);
        Assert.Equal(lead.Id, entry.EntityId);
        Assert.Equal(AuditAction.AngebotCustomerApproved, entry.Action);

        // The actor is a customer with no account — ERD.md's own meaning for a null PerformedByUserId.
        Assert.Null(entry.PerformedByUserId);
    }

    [Fact]
    public async Task HandleAsync_Rejection_AuditsTheRejectionAction()
    {
        await SeedSentAngebotAsync();

        await _handler.HandleAsync(
            new RecordAngebotDecisionCommand(Token, CustomerDecision.Reject), CancellationToken.None);

        Assert.Equal(AuditAction.AngebotCustomerRejected, Assert.Single(_auditService.Calls).Action);
    }

    /// <summary>SRS FR-9.2's third Admin trigger.</summary>
    [Fact]
    public async Task HandleAsync_NotifiesTheAdminOfTheOutcome()
    {
        var (angebot, lead, _) = await SeedSentAngebotAsync();

        await _handler.HandleAsync(
            new RecordAngebotDecisionCommand(Token, CustomerDecision.Approve), CancellationToken.None);

        var notification = Assert.Single(_emailSender.AngebotDecisionNotifications);
        Assert.True(notification.Approved);
        Assert.Equal(angebot.AngebotNumber, notification.AngebotNumber);
        Assert.Equal(lead.Id, notification.LeadId);
    }

    [Fact]
    public async Task HandleAsync_Rejection_NotifiesTheAdminThatItWasRejected()
    {
        await SeedSentAngebotAsync();

        await _handler.HandleAsync(
            new RecordAngebotDecisionCommand(Token, CustomerDecision.Reject), CancellationToken.None);

        Assert.False(Assert.Single(_emailSender.AngebotDecisionNotifications).Approved);
    }

    // ---- BR-4 and Sequence Diagram §12 --------------------------------------------------------

    /// <summary>
    /// BR-4: a forwarded or leaked link must not be able to flip a decision after the fact. The
    /// guard lives in <c>TokenLink.MarkUsed()</c>, so this surfaces as an
    /// <see cref="InvalidOperationException"/> — 409, not 410: the link still exists and stays
    /// readable, what conflicts is the decision already recorded.
    /// </summary>
    [Fact]
    public async Task HandleAsync_OnAnAlreadyDecidedLink_Throws()
    {
        await SeedSentAngebotAsync();
        await _handler.HandleAsync(
            new RecordAngebotDecisionCommand(Token, CustomerDecision.Approve), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(
                new RecordAngebotDecisionCommand(Token, CustomerDecision.Reject), CancellationToken.None));
    }

    /// <summary>
    /// The reuse attempt must change nothing at all — not the recorded outcome, not the Lead, and
    /// no second audit entry or notification. This is what makes BR-4 a guarantee rather than a
    /// status code.
    /// </summary>
    [Fact]
    public async Task HandleAsync_OnAnAlreadyDecidedLink_ChangesNothing()
    {
        var (angebot, lead, _) = await SeedSentAngebotAsync();
        await _handler.HandleAsync(
            new RecordAngebotDecisionCommand(Token, CustomerDecision.Approve), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(
                new RecordAngebotDecisionCommand(Token, CustomerDecision.Reject), CancellationToken.None));

        Assert.Equal(AngebotStatus.CustomerApproved, angebot.Status);
        Assert.Equal(LeadStatus.Won, lead.Status);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        Assert.Single(_auditService.Calls);
        Assert.Single(_emailSender.AngebotDecisionNotifications);
    }

    [Fact]
    public async Task HandleAsync_ForAnUnknownToken_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(
                new RecordAngebotDecisionCommand("no-such-token", CustomerDecision.Approve), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ForAnInvoiceToken_ThrowsTheSameNotFoundAsAnUnknownToken()
    {
        var link = TokenLink.Create(TokenLinkEntityType.Invoice, 1, Token, DateTime.UtcNow.AddDays(30));
        await _tokenLinkRepository.AddAsync(link, CancellationToken.None);

        var wrongType = await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(
                new RecordAngebotDecisionCommand(Token, CustomerDecision.Approve), CancellationToken.None));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(
                new RecordAngebotDecisionCommand("no-such-token", CustomerDecision.Approve), CancellationToken.None));

        Assert.Equal(unknown.Message, wrongType.Message);
    }

    [Fact]
    public async Task HandleAsync_ForAnExpiredToken_ThrowsGone()
    {
        var lead = _leadRepository.Seed(Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Website));
        var angebot = _angebotRepository.Seed(Angebot.Create(lead.Id, null, "ANG-2026-00043", OwningInspectorId));
        var link = TokenLink.Create(TokenLinkEntityType.Angebot, angebot.Id, Token, DateTime.UtcNow.AddMilliseconds(50));
        await _tokenLinkRepository.AddAsync(link, CancellationToken.None);
        Thread.Sleep(120);

        await Assert.ThrowsAsync<GoneException>(
            () => _handler.HandleAsync(
                new RecordAngebotDecisionCommand(Token, CustomerDecision.Approve), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_NeverPutsTheTokenInTheFailureMessage()
    {
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(
                new RecordAngebotDecisionCommand("a-secret-token-value", CustomerDecision.Approve), CancellationToken.None));

        Assert.DoesNotContain("a-secret-token-value", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An Angebot that was never sent has no valid token in practice, but the aggregate's own guard
    /// is what makes that structural rather than incidental — and a rejected attempt must leave the
    /// link unconsumed, or a data problem would cost the customer their one chance to answer.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenTheAngebotWasNeverSent_ThrowsAndLeavesTheLinkUnused()
    {
        var lead = _leadRepository.Seed(Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Website));
        lead.MarkInspectionScheduled();
        lead.MarkInspectionDone();
        lead.MarkAngebotInProgress();
        var angebot = _angebotRepository.Seed(Angebot.Create(lead.Id, null, "ANG-2026-00044", OwningInspectorId));
        var link = TokenLink.Create(TokenLinkEntityType.Angebot, angebot.Id, Token, DateTime.UtcNow.AddDays(30));
        await _tokenLinkRepository.AddAsync(link, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(
                new RecordAngebotDecisionCommand(Token, CustomerDecision.Approve), CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
        Assert.Empty(_emailSender.AngebotDecisionNotifications);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WithABlankToken_ThrowsValidationException(string token)
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(
                new RecordAngebotDecisionCommand(token, CustomerDecision.Approve), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WithAnUndefinedDecision_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(
                new RecordAngebotDecisionCommand(Token, (CustomerDecision)99), CancellationToken.None));
    }
}
