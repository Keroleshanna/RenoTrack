using FluentValidation;
using RenoTrack.Application.Angebote.Commands.SendAngebot;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Commands.SendAngebot;

public class SendAngebotCommandHandlerTests
{
    private const int OwningInspectorId = 5;
    private const int AdminId = 2;

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly FakeLeadRepository _leadRepository = new();
    private readonly FakeTokenLinkRepository _tokenLinkRepository = new();
    private readonly FakeTokenLinkService _tokenLinkService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly FakeEmailSender _emailSender = new();
    private readonly SendAngebotCommandHandler _handler;

    public SendAngebotCommandHandlerTests()
    {
        _handler = new SendAngebotCommandHandler(
            new SendAngebotCommandValidator(),
            _angebotRepository,
            _leadRepository,
            _tokenLinkRepository,
            _tokenLinkService,
            _unitOfWork,
            _auditService,
            _emailSender);
    }

    /// <summary>
    /// Drives both aggregates to the real state this command starts from, entirely through their own
    /// transition methods — never by seeding a status directly. The Lead's path to
    /// <c>AngebotInProgress</c> is exactly the one production takes, which is what makes the
    /// MarkAngebotSent guard meaningful here.
    /// </summary>
    private (Angebot Angebot, Lead Lead) SeedApprovedAngebotAndLead()
    {
        var lead = _leadRepository.Seed(Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Website));
        lead.MarkInspectionScheduled();
        lead.MarkInspectionDone();
        lead.MarkAngebotInProgress();

        var angebot = _angebotRepository.Seed(Angebot.Create(lead.Id, inspectionId: null, "ANG-2026-00001", OwningInspectorId));
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);
        angebot.SubmitForReview();
        angebot.Approve(AdminId);

        return (angebot, lead);
    }

    // ---- Happy path -------------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_TransitionsAngebotToSent()
    {
        var (angebot, _) = SeedApprovedAngebotAndLead();

        var result = await _handler.HandleAsync(new SendAngebotCommand(angebot.Id, AdminId), CancellationToken.None);

        Assert.Equal(AngebotStatus.Sent, result.Status);
    }

    [Fact]
    public async Task HandleAsync_StampsSentAt()
    {
        var (angebot, _) = SeedApprovedAngebotAndLead();

        await _handler.HandleAsync(new SendAngebotCommand(angebot.Id, AdminId), CancellationToken.None);

        Assert.NotNull(angebot.SentAt);
    }

    /// <summary>
    /// StateMachine.md §1.3: the Lead moves too. This is the transition that finally makes
    /// <c>LeadStatus.AngebotSent</c> reachable — before this slice, <c>Lead.MarkAngebotSent()</c>
    /// was called by nothing, which is why Won/Lost had no reachable happy path.
    /// </summary>
    [Fact]
    public async Task HandleAsync_TransitionsLeadToAngebotSent()
    {
        var (angebot, lead) = SeedApprovedAngebotAndLead();

        await _handler.HandleAsync(new SendAngebotCommand(angebot.Id, AdminId), CancellationToken.None);

        Assert.Equal(LeadStatus.AngebotSent, lead.Status);
    }

    [Fact]
    public async Task HandleAsync_IssuesExactlyOneTokenLinkForThisAngebot()
    {
        var (angebot, _) = SeedApprovedAngebotAndLead();

        await _handler.HandleAsync(new SendAngebotCommand(angebot.Id, AdminId), CancellationToken.None);

        var tokenLink = Assert.Single(_tokenLinkRepository.AddedTokenLinks);
        Assert.Equal(TokenLinkEntityType.Angebot, tokenLink.EntityType);
        Assert.Equal(angebot.Id, tokenLink.EntityId);
        Assert.Null(tokenLink.UsedAt);
    }

    /// <summary>
    /// One commit covers the Angebot transition, the Lead transition and the token row. A second
    /// SaveChangesAsync would mean they could land separately — a live customer credential for a
    /// document that was never marked sent, or a sent Angebot the customer can never open.
    /// </summary>
    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        var (angebot, _) = SeedApprovedAngebotAndLead();

        await _handler.HandleAsync(new SendAngebotCommand(angebot.Id, AdminId), CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    // ---- Audit ------------------------------------------------------------------------------

    /// <summary>
    /// Logged against Lead, not Angebot (CLAUDE.md §10): the business-meaningful transition here is
    /// the Lead reaching AngebotSent, matching how AngebotCreated is logged for MarkAngebotInProgress.
    /// </summary>
    [Fact]
    public async Task HandleAsync_LogsTheAuditEntryAgainstTheLead()
    {
        var (angebot, lead) = SeedApprovedAngebotAndLead();

        await _handler.HandleAsync(new SendAngebotCommand(angebot.Id, AdminId), CancellationToken.None);

        var entry = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(Lead), entry.EntityType);
        Assert.Equal(lead.Id, entry.EntityId);
        Assert.Equal(AuditAction.AngebotSent, entry.Action);
        Assert.Equal(AdminId, entry.PerformedByUserId);
    }

    // ---- Notification -----------------------------------------------------------------------

    /// <summary>
    /// SRS FR-9.1: the customer gets the link. The token in the notification must be the one that
    /// was actually persisted — sending a different token would deliver a dead link.
    /// </summary>
    [Fact]
    public async Task HandleAsync_EmailsTheLeadTheTokenThatWasPersisted()
    {
        var (angebot, lead) = SeedApprovedAngebotAndLead();

        await _handler.HandleAsync(new SendAngebotCommand(angebot.Id, AdminId), CancellationToken.None);

        var notification = Assert.Single(_emailSender.AngebotReadyNotifications);
        var persisted = Assert.Single(_tokenLinkRepository.AddedTokenLinks);
        Assert.Equal(persisted.Token, notification.Token);
        Assert.Equal(lead.Email, notification.RecipientEmail);
        Assert.Equal(angebot.AngebotNumber, notification.AngebotNumber);
    }

    // ---- Guard failures ---------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_WhenTheAngebotDoesNotExist_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new SendAngebotCommand(999, AdminId), CancellationToken.None));
    }

    /// <summary>
    /// StateMachine.md §2.3 allows Send only from ApprovedInternally — an Angebot still awaiting
    /// internal review must never reach the customer (BR-1).
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenTheAngebotIsStillInReview_Throws()
    {
        var lead = _leadRepository.Seed(Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Website));
        lead.MarkInspectionScheduled();
        lead.MarkInspectionDone();
        lead.MarkAngebotInProgress();

        var angebot = _angebotRepository.Seed(Angebot.Create(lead.Id, null, "ANG-2026-00002", OwningInspectorId));
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);
        angebot.SubmitForReview();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(new SendAngebotCommand(angebot.Id, AdminId), CancellationToken.None));
    }

    /// <summary>
    /// Sending twice must not issue a second token link — BR-4's single-use guarantee would be
    /// meaningless if a fresh link could simply be minted for the same Angebot.
    /// </summary>
    [Fact]
    public async Task HandleAsync_SendingTwice_IssuesNoSecondTokenLink()
    {
        var (angebot, _) = SeedApprovedAngebotAndLead();
        await _handler.HandleAsync(new SendAngebotCommand(angebot.Id, AdminId), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(new SendAngebotCommand(angebot.Id, AdminId), CancellationToken.None));

        Assert.Single(_tokenLinkRepository.AddedTokenLinks);
    }

    /// <summary>
    /// A rejected send must leave nothing behind: no token generated, nothing committed, no email.
    /// This is the §12 ordering principle — every guard runs before any side effect.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenRejected_GeneratesNoTokenAndSendsNoEmail()
    {
        var lead = _leadRepository.Seed(Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Website));
        lead.MarkInspectionScheduled();
        lead.MarkInspectionDone();
        lead.MarkAngebotInProgress();
        var angebot = _angebotRepository.Seed(Angebot.Create(lead.Id, null, "ANG-2026-00003", OwningInspectorId));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(new SendAngebotCommand(angebot.Id, AdminId), CancellationToken.None));

        Assert.Equal(0, _tokenLinkService.CallCount);
        Assert.Empty(_tokenLinkRepository.AddedTokenLinks);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_emailSender.AngebotReadyNotifications);
        Assert.Empty(_auditService.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task HandleAsync_WithAnInvalidAngebotId_ThrowsValidationException(int angebotId)
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new SendAngebotCommand(angebotId, AdminId), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WithAnInvalidAdminId_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new SendAngebotCommand(1, SentByAdminId: 0), CancellationToken.None));
    }
}
