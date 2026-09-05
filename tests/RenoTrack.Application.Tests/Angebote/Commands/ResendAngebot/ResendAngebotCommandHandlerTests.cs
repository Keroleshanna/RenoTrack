using FluentValidation;
using RenoTrack.Application.Angebote.Commands.ResendAngebot;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Commands.ResendAngebot;

/// <summary>
/// FR-6.1a / <b>D99</b>. Re-issuing the customer's token link: the previous one is superseded and
/// its replacement created in one commit.
/// </summary>
/// <remarks>
/// The concurrency guarantee this slice rests on cannot be shown here — an in-memory fake has no
/// <c>WHERE</c> clause to miss. It is proven against real SQL Server in
/// <c>RenoTrack.Infrastructure.Tests</c>, which is where a race can actually happen. What these
/// tests own is the orchestration: what is written, in what order, and what is *not* written when
/// the handler refuses.
/// </remarks>
public class ResendAngebotCommandHandlerTests
{
    private const int OwningInspectorId = 5;
    private const int AdminId = 2;
    private const string OriginalToken = "original-token";

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly FakeLeadRepository _leadRepository = new();
    private readonly FakeTokenLinkRepository _tokenLinkRepository = new();
    private readonly FakeTokenLinkService _tokenLinkService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly FakeEmailSender _emailSender = new();
    private readonly ResendAngebotCommandHandler _handler;

    public ResendAngebotCommandHandlerTests() =>
        _handler = new ResendAngebotCommandHandler(
            new ResendAngebotCommandValidator(),
            _angebotRepository,
            _leadRepository,
            _tokenLinkRepository,
            _tokenLinkService,
            _unitOfWork,
            _auditService,
            _emailSender);

    /// <summary>
    /// Drives both aggregates to the state a real send leaves them in, through their own
    /// transitions, and issues the link that send would have issued.
    /// </summary>
    private async Task<(Angebot Angebot, Lead Lead, TokenLink Link)> SeedSentAngebotAsync()
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

        var link = TokenLink.Create(TokenLinkEntityType.Angebot, angebot.Id, OriginalToken, DateTime.UtcNow.AddDays(30));
        await _tokenLinkRepository.AddAsync(link, CancellationToken.None);

        return (angebot, lead, link);
    }

    private ResendAngebotCommand CommandFor(Angebot angebot) => new(angebot.Id, AdminId);

    // ---- The successful re-issue -------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_SupersedesTheOldLinkAndCreatesExactlyOneReplacement()
    {
        var (angebot, _, original) = await SeedSentAngebotAsync();

        await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        Assert.True(original.IsExpired(DateTime.UtcNow));
        Assert.Null(original.UsedAt);

        Assert.Equal(2, _tokenLinkRepository.AddedTokenLinks.Count);
        var usable = _tokenLinkRepository.AddedTokenLinks
            .Where(link => link.UsedAt is null && !link.IsExpired(DateTime.UtcNow))
            .ToList();
        Assert.Single(usable);
        Assert.NotEqual(OriginalToken, usable[0].Token);
    }

    /// <summary>
    /// One commit, so the supersession and the replacement can never be observed apart. A second
    /// SaveChanges would allow a live replacement whose predecessor was never invalidated — the
    /// exact invariant this slice exists to hold.
    /// </summary>
    [Fact]
    public async Task HandleAsync_CommitsTheSupersessionAndTheReplacementTogether()
    {
        var (angebot, _, _) = await SeedSentAngebotAsync();

        await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    /// <summary>D99 Q4: SentAt records the original send, and a re-issue is not one.</summary>
    [Fact]
    public async Task HandleAsync_LeavesSentAtAndBothStatusesUntouched()
    {
        var (angebot, lead, _) = await SeedSentAngebotAsync();
        var sentAtBefore = angebot.SentAt;

        await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        Assert.Equal(sentAtBefore, angebot.SentAt);
        Assert.Equal(AngebotStatus.Sent, angebot.Status);
        Assert.Equal(LeadStatus.AngebotSent, lead.Status);
    }

    /// <summary>
    /// Against <c>Angebot</c>, not <c>Lead</c> — the opposite of <c>AngebotSent</c>, because no
    /// Lead-level milestone occurred. This entry is also the only record that a re-issue happened,
    /// since <c>SentAt</c> is deliberately unchanged.
    /// </summary>
    [Fact]
    public async Task HandleAsync_AuditsAgainstTheAngebotAfterTheCommit()
    {
        var (angebot, _, _) = await SeedSentAngebotAsync();

        await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        var entry = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(Angebot), entry.EntityType);
        Assert.Equal(angebot.Id, entry.EntityId);
        Assert.Equal(AuditAction.AngebotLinkReissued, entry.Action);
        Assert.Equal(AdminId, entry.PerformedByUserId);
    }

    /// <summary>
    /// The same notification the original send used — Q3 required no second email mechanism — and
    /// it carries the <b>new</b> token. Mailing the superseded one would hand the customer a link
    /// that is already dead.
    /// </summary>
    [Fact]
    public async Task HandleAsync_EmailsTheCustomerTheNewTokenAndNotTheOldOne()
    {
        var (angebot, lead, _) = await SeedSentAngebotAsync();

        await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        var notification = Assert.Single(_emailSender.AngebotReadyNotifications);
        Assert.Equal(lead.Email, notification.RecipientEmail);
        Assert.NotEqual(OriginalToken, notification.Token);

        var replacement = _tokenLinkRepository.AddedTokenLinks.Last();
        Assert.Equal(replacement.Token, notification.Token);
    }

    /// <summary>Q2: a link that lapsed before the customer answered is the most valuable case.</summary>
    [Fact]
    public async Task HandleAsync_ReIssuesEvenWhenTheExistingLinkHasAlreadyLapsed()
    {
        var (angebot, _, _) = await SeedSentAngebotAsync();
        _tokenLinkRepository.AddedTokenLinks.Clear();

        var lapsed = TokenLink.Create(
            TokenLinkEntityType.Angebot, angebot.Id, "lapsed-token", DateTime.UtcNow.AddMilliseconds(50));
        await _tokenLinkRepository.AddAsync(lapsed, CancellationToken.None);
        Thread.Sleep(120);

        await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        Assert.Equal(2, _tokenLinkRepository.AddedTokenLinks.Count);
        Assert.Single(_emailSender.AngebotReadyNotifications);
    }

    // ---- Refusals, and what they must not leave behind ---------------------------------------

    /// <summary>
    /// The Sent-only rule (D99 Q1). Asserted as "nothing was written" rather than only as "it
    /// threw": a refusal that had already staged a replacement would be worse than the refusal.
    /// </summary>
    [Theory]
    [InlineData(AngebotStatus.Draft)]
    [InlineData(AngebotStatus.InReview)]
    [InlineData(AngebotStatus.ChangesRequested)]
    [InlineData(AngebotStatus.ApprovedInternally)]
    [InlineData(AngebotStatus.CustomerApproved)]
    [InlineData(AngebotStatus.CustomerRejected)]
    public async Task HandleAsync_RefusesAnyAngebotThatIsNotSent(AngebotStatus status)
    {
        var (angebot, _, _) = await SeedSentAngebotAsync();
        DriveTo(angebot, status);
        var linksBefore = _tokenLinkRepository.AddedTokenLinks.Count;

        await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(CommandFor(angebot), CancellationToken.None));

        Assert.Equal(linksBefore, _tokenLinkRepository.AddedTokenLinks.Count);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
        Assert.Empty(_emailSender.AngebotReadyNotifications);
    }

    /// <summary>
    /// BR-4: a link that already carried a decision is terminal, so there is nothing to supersede.
    /// The aggregate refuses, and the handler must not have staged a replacement first.
    /// </summary>
    [Fact]
    public async Task HandleAsync_RefusesWhenTheExistingLinkAlreadyCarriedADecision()
    {
        var (angebot, _, original) = await SeedSentAngebotAsync();
        original.MarkUsed();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(CommandFor(angebot), CancellationToken.None));

        Assert.Single(_tokenLinkRepository.AddedTokenLinks);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_ThrowsNotFoundForAnUnknownAngebot()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new ResendAngebotCommand(9_999, AdminId), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_RefusesAnAngebotWithNoLinkToReIssue()
    {
        var (angebot, _, _) = await SeedSentAngebotAsync();
        _tokenLinkRepository.AddedTokenLinks.Clear();

        await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(CommandFor(angebot), CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task HandleAsync_RejectsANonPositiveAngebotId(int angebotId)
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new ResendAngebotCommand(angebotId, AdminId), CancellationToken.None));
    }

    /// <summary>
    /// Drives the Angebot to a status other than <c>Sent</c> through its own transitions, so no
    /// test reaches past the aggregate to fabricate a state it could not really be in.
    /// </summary>
    private static void DriveTo(Angebot angebot, AngebotStatus status)
    {
        switch (status)
        {
            case AngebotStatus.CustomerApproved:
                angebot.RecordCustomerApproval();
                break;

            case AngebotStatus.CustomerRejected:
                angebot.RecordCustomerRejection();
                break;

            default:
                // Draft, InReview, ChangesRequested and ApprovedInternally all precede Sent, so a
                // fresh Angebot is driven forward only as far as the requested state.
                var fresh = Angebot.Create(angebot.LeadId, null, "ANG-2026-00043", OwningInspectorId);
                var section = fresh.AddSection("Pos. 1", 1);
                fresh.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(100.00m), VatRate.Standard);

                if (status != AngebotStatus.Draft)
                {
                    fresh.SubmitForReview();
                }

                if (status == AngebotStatus.ChangesRequested)
                {
                    fresh.RequestChanges(AdminId);
                }

                if (status == AngebotStatus.ApprovedInternally)
                {
                    fresh.Approve(AdminId);
                }

                CopyStatusOnto(angebot, fresh.Status);
                break;
        }
    }

    /// <summary>
    /// The four pre-<c>Sent</c> states cannot be reached from <c>Sent</c> by any real transition,
    /// so the status is reflected onto the seeded aggregate. Reflection in the test only, never in
    /// production code (CLAUDE.md §14) — and only for a value the aggregate genuinely reached on a
    /// sibling instance a moment earlier, driven through its own methods.
    /// </summary>
    private static void CopyStatusOnto(Angebot angebot, AngebotStatus status) =>
        typeof(Angebot).GetProperty(nameof(Angebot.Status))!.SetValue(angebot, status);
}
