using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Email;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Entities;
using RenoTrack.Infrastructure.Tests.Persistence;
using RenoTrack.Infrastructure.TokenLinks;

namespace RenoTrack.Infrastructure.Tests.Email;

/// <summary>
/// Manual retry (Phase 9 Slice 5, D70) against real LocalDB and a real socket — the two things this
/// slice's claims actually depend on. The compare-and-set claim is a database guarantee, so proving
/// it against the EF InMemory provider would prove nothing (D40).
/// </summary>
/// <remarks>
/// Behavioural throughout: every assertion is made against the persisted row or the observable
/// outcome, never against how the service reached it.
/// </remarks>
[Collection("Infrastructure Database")]
public sealed class NotificationRetryServiceTests(RenoTrackDbContextFixture fixture)
{
    private static int NextEntityId() => Random.Shared.Next(100_000, 999_999);

    // ---------- refusals that never touch SMTP ----------

    [Fact]
    public async Task An_unknown_delivery_is_not_found()
    {
        await using var context = fixture.CreateContext();
        var service = CreateService(context, EnabledOptions(port: 25));

        await Assert.ThrowsAsync<NotFoundException>(() => service.RetryAsync(int.MaxValue, CancellationToken.None));
    }

    /// <summary>
    /// S5-9: a disabled deployment is a state conflict, not a transient outage — and it must be
    /// refused <b>before</b> the claim, so a switched-off host cannot inflate <c>AttemptCount</c>.
    /// </summary>
    [Fact]
    public async Task Retry_is_refused_when_email_delivery_is_disabled()
    {
        var delivery = await SeedDeliveryAsync(NotificationType.NewWebsiteLead, "Lead", NextEntityId());

        await using var context = fixture.CreateContext();
        var service = CreateService(context, new EmailOptions { Enabled = false });

        await Assert.ThrowsAsync<ConflictException>(() => service.RetryAsync(delivery.Id, CancellationToken.None));

        var stored = await ReloadAsync(delivery.Id);
        Assert.Equal(NotificationDeliveryStatus.Pending, stored.Status);
        Assert.Equal(1, stored.AttemptCount);
    }

    [Fact]
    public async Task A_sent_delivery_is_never_retryable()
    {
        var delivery = await SeedDeliveryAsync(
            NotificationType.NewWebsiteLead, "Lead", NextEntityId(), d => d.MarkSent(DateTime.UtcNow));

        await using var context = fixture.CreateContext();
        var service = CreateService(context, EnabledOptions(port: 25));

        await Assert.ThrowsAsync<ConflictException>(() => service.RetryAsync(delivery.Id, CancellationToken.None));

        var stored = await ReloadAsync(delivery.Id);
        Assert.Equal(NotificationDeliveryStatus.Sent, stored.Status);
        Assert.Equal(1, stored.AttemptCount);
    }

    /// <summary>
    /// The double-click guard (S5-2). Two claims run against the same row; exactly one may win, and
    /// the loser must be refused rather than delivering a second copy.
    /// </summary>
    [Fact]
    public async Task Only_one_of_two_competing_retries_claims_the_delivery()
    {
        await using var server = new InProcessSmtpServer();
        var lead = await SeedLeadAsync();
        var delivery = await SeedDeliveryAsync(
            NotificationType.NewWebsiteLead, nameof(Lead), lead.Id, d => d.MarkFailed(DateTime.UtcNow, "X", "y"));

        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var first = CreateService(firstContext, EnabledOptions(server.Port));
        var second = CreateService(secondContext, EnabledOptions(server.Port));

        var firstResult = await Record.ExceptionAsync(() => first.RetryAsync(delivery.Id, CancellationToken.None));
        var secondResult = await Record.ExceptionAsync(() => second.RetryAsync(delivery.Id, CancellationToken.None));

        // The first claim moved the row out of every retryable state and on to Sent, so the second
        // matches no row. One winner, one 409 — never two deliveries.
        Assert.Null(firstResult);
        Assert.IsType<ConflictException>(secondResult);

        var stored = await ReloadAsync(delivery.Id);
        Assert.Equal(NotificationDeliveryStatus.Sent, stored.Status);
        Assert.Equal(2, stored.AttemptCount);
    }

    // ---------- the three retryable states ----------

    [Theory]
    [InlineData(NotificationDeliveryStatus.Pending)]
    [InlineData(NotificationDeliveryStatus.Failed)]
    [InlineData(NotificationDeliveryStatus.Sending)]
    public async Task Every_retryable_state_reaches_Sent(NotificationDeliveryStatus startingStatus)
    {
        await using var server = new InProcessSmtpServer();
        var lead = await SeedLeadAsync();
        var delivery = await SeedDeliveryAsync(NotificationType.NewWebsiteLead, nameof(Lead), lead.Id);
        await ForceStatusAsync(delivery.Id, startingStatus);

        await using var context = fixture.CreateContext();
        var service = CreateService(context, EnabledOptions(server.Port));

        var result = await service.RetryAsync(delivery.Id, CancellationToken.None);

        Assert.Equal(NotificationDeliveryStatus.Sent, result.Status);
        Assert.Equal(NotificationDeliveryStatus.Sent, (await ReloadAsync(delivery.Id)).Status);
    }

    /// <summary>
    /// A stranded <c>Sending</c> row is recoverable **only** by hand (S5-3). There is no lease and no
    /// sweeper, so this test is what stands between a crashed attempt and a permanently stuck row.
    /// </summary>
    [Fact]
    public async Task A_stranded_Sending_row_is_recoverable_by_a_manual_retry()
    {
        await using var server = new InProcessSmtpServer();
        var lead = await SeedLeadAsync();
        var delivery = await SeedDeliveryAsync(NotificationType.NewWebsiteLead, nameof(Lead), lead.Id);
        await ForceStatusAsync(delivery.Id, NotificationDeliveryStatus.Sending);

        await using var context = fixture.CreateContext();
        var service = CreateService(context, EnabledOptions(server.Port));

        var result = await service.RetryAsync(delivery.Id, CancellationToken.None);

        Assert.Equal(NotificationDeliveryStatus.Sent, result.Status);
    }

    // ---------- the claim's bookkeeping survives the terminal write ----------

    /// <summary>
    /// The regression this slice's ordering rule exists to prevent (S5-2). The claim increments
    /// <c>AttemptCount</c> at the database via <c>ExecuteUpdateAsync</c>, which bypasses the change
    /// tracker; if the service loaded the row <em>before</em> claiming, the terminal
    /// <c>SaveChangesAsync</c> would write the stale count back and silently undo the increment.
    /// </summary>
    [Fact]
    public async Task The_claims_attempt_count_increment_survives_the_terminal_update()
    {
        await using var server = new InProcessSmtpServer();
        var lead = await SeedLeadAsync();
        var delivery = await SeedDeliveryAsync(
            NotificationType.NewWebsiteLead, nameof(Lead), lead.Id, d => d.MarkFailed(DateTime.UtcNow, "X", "y"));

        await using var context = fixture.CreateContext();
        var service = CreateService(context, EnabledOptions(server.Port));

        var before = await ReloadAsync(delivery.Id);
        var result = await service.RetryAsync(delivery.Id, CancellationToken.None);

        Assert.Equal(before.AttemptCount + 1, result.AttemptCount);

        // Asserted against the database, not the returned DTO: the whole hazard is a stale in-memory
        // value being written back over the claim.
        Assert.Equal(before.AttemptCount + 1, (await ReloadAsync(delivery.Id)).AttemptCount);
    }

    [Fact]
    public async Task Retry_advances_LastAttemptAt()
    {
        await using var server = new InProcessSmtpServer();
        var lead = await SeedLeadAsync();
        var delivery = await SeedDeliveryAsync(NotificationType.NewWebsiteLead, nameof(Lead), lead.Id);

        var before = (await ReloadAsync(delivery.Id)).LastAttemptAt;
        await Task.Delay(10);

        await using var context = fixture.CreateContext();
        await CreateService(context, EnabledOptions(server.Port)).RetryAsync(delivery.Id, CancellationToken.None);

        var after = (await ReloadAsync(delivery.Id)).LastAttemptAt;
        Assert.NotNull(after);
        Assert.True(after > before, $"LastAttemptAt did not advance: {before:O} → {after:O}");
    }

    /// <summary>S5-1: a retry updates the existing row or it does nothing. It never inserts.</summary>
    [Fact]
    public async Task Retry_reuses_the_existing_row_and_creates_no_second_delivery()
    {
        await using var server = new InProcessSmtpServer();
        var lead = await SeedLeadAsync();
        var delivery = await SeedDeliveryAsync(NotificationType.NewWebsiteLead, nameof(Lead), lead.Id);

        await using var context = fixture.CreateContext();
        await CreateService(context, EnabledOptions(server.Port)).RetryAsync(delivery.Id, CancellationToken.None);

        await using var readContext = fixture.CreateContext();
        var rowsForThisLead = await readContext.NotificationDeliveries
            .Where(d => d.EntityType == nameof(Lead) && d.EntityId == lead.Id)
            .ToListAsync();

        var only = Assert.Single(rowsForThisLead);
        Assert.Equal(delivery.Id, only.Id);
    }

    // ---------- reconstruction ----------

    /// <summary>
    /// S5-5: the recipient is rebuilt from configuration at retry time, never read back from the
    /// row. Here the configured Admin list has changed since the original attempt, and the retry
    /// must follow the new one.
    /// </summary>
    [Fact]
    public async Task The_recipient_is_re_resolved_rather_than_reused()
    {
        await using var server = new InProcessSmtpServer();
        var lead = await SeedLeadAsync();
        var delivery = await SeedDeliveryAsync(
            NotificationType.NewWebsiteLead,
            nameof(Lead),
            lead.Id,
            d => d.RecordRecipient("stale-office@example.invalid"));

        await using var context = fixture.CreateContext();
        var options = EnabledOptions(server.Port, adminRecipients: ["neu@example.invalid", "inhaber@example.invalid"]);

        await CreateService(context, options).RetryAsync(delivery.Id, CancellationToken.None);

        var stored = await ReloadAsync(delivery.Id);
        Assert.Equal("neu@example.invalid, inhaber@example.invalid", stored.Recipient);
        Assert.DoesNotContain("stale-office", stored.Recipient);
    }

    /// <summary>
    /// S5-4, including the imprecision it accepts: the delivery row names the Angebot, never the
    /// comment, so a retry sends whichever comment is newest now.
    /// </summary>
    [Fact]
    public async Task Changes_requested_reconstructs_the_latest_review_comment()
    {
        await using var server = new InProcessSmtpServer();
        var inspectorId = await SeedInspectorAsync("retry-inspector@example.invalid");
        var angebot = await SeedAngebotAsync(inspectorId);

        await SeedReviewCommentAsync(angebot.Id, "Erste Anmerkung");
        await SeedReviewCommentAsync(angebot.Id, "Neueste Anmerkung");

        var delivery = await SeedDeliveryAsync(
            NotificationType.AngebotChangesRequested, nameof(Angebot), angebot.Id);

        await using var context = fixture.CreateContext();
        await CreateService(context, EnabledOptions(server.Port)).RetryAsync(delivery.Id, CancellationToken.None);

        var body = Assert.Single(server.Messages);
        Assert.Contains("Neueste Anmerkung", body);
        Assert.DoesNotContain("Erste Anmerkung", body);
    }

    /// <summary>
    /// D2 is unchanged by retry: a missing Inspector address is a <b>preparation failure recorded on
    /// the row</b>, not a refusal. It must not become a 409 — the delivery genuinely was attempted.
    /// </summary>
    [Fact]
    public async Task A_missing_inspector_address_stays_a_preparation_failure_not_a_refusal()
    {
        await using var server = new InProcessSmtpServer();

        // A real Inspector row carrying no address — `AspNetUsers.Email` is nullable, and D2 treats
        // that as a delivery failure. A fabricated id would not do: `Angebote.CreatedByInspectorId`
        // has a real foreign key (Phase 3 Slice 15), so it would fail on insert instead.
        var inspectorId = await SeedInspectorAsync(email: null);
        var angebot = await SeedAngebotAsync(createdByInspectorId: inspectorId);
        await SeedReviewCommentAsync(angebot.Id, "Bitte korrigieren");

        var delivery = await SeedDeliveryAsync(
            NotificationType.AngebotChangesRequested, nameof(Angebot), angebot.Id);

        await using var context = fixture.CreateContext();
        var result = await CreateService(context, EnabledOptions(server.Port))
            .RetryAsync(delivery.Id, CancellationToken.None);

        Assert.Equal(NotificationDeliveryStatus.Failed, result.Status);
        Assert.Equal(nameof(InvalidOperationException), result.FailureType);
        Assert.Equal("The notification could not be prepared.", result.FailureMessage);
    }

    // ---------- staleness refusals (S5-6) ----------

    [Theory]
    [InlineData(true, false, "expired")]
    [InlineData(false, true, "already been used")]
    public async Task An_unusable_angebot_token_refuses_without_touching_the_row(
        bool expired, bool used, string expectedReason)
    {
        var angebot = await SeedAngebotAsync();
        await SeedTokenLinkAsync(TokenLinkEntityType.Angebot, angebot.Id, expired: expired, used: used);
        var delivery = await SeedDeliveryAsync(NotificationType.AngebotReady, nameof(Angebot), angebot.Id);

        await AssertRefusesWithoutMutatingAsync(delivery.Id, expectedReason);
    }

    [Theory]
    [InlineData(true, false, "expired")]
    [InlineData(false, true, "already been used")]
    public async Task An_unusable_invoice_token_refuses_without_touching_the_row(
        bool expired, bool used, string expectedReason)
    {
        var invoice = await SeedInvoiceAsync(InvoiceStatus.Sent);
        await SeedTokenLinkAsync(TokenLinkEntityType.Invoice, invoice.Id, expired: expired, used: used);
        var delivery = await SeedDeliveryAsync(NotificationType.InvoiceReady, nameof(Invoice), invoice.Id);

        await AssertRefusesWithoutMutatingAsync(delivery.Id, expectedReason);
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Void)]
    public async Task A_paid_or_void_invoice_refuses_without_touching_the_row(InvoiceStatus status)
    {
        var invoice = await SeedInvoiceAsync(status);
        await SeedTokenLinkAsync(TokenLinkEntityType.Invoice, invoice.Id);
        var delivery = await SeedDeliveryAsync(NotificationType.InvoiceReady, nameof(Invoice), invoice.Id);

        await AssertRefusesWithoutMutatingAsync(delivery.Id, status.ToString());
    }

    /// <summary>
    /// The regression that S5-10 exists to prevent. An earlier implementation claimed the row first
    /// and then marked a refused retry <c>Failed</c> — which made a permanently-invalid notification
    /// permanently <em>retryable</em> (S5-3 makes <c>Failed</c> retryable), so an Admin clicking a
    /// doomed row could drive <c>AttemptCount</c> upward without limit on something that can never
    /// succeed.
    /// </summary>
    [Fact]
    public async Task Repeatedly_retrying_a_permanently_stale_notification_never_mutates_the_row()
    {
        var angebot = await SeedAngebotAsync();
        await SeedTokenLinkAsync(TokenLinkEntityType.Angebot, angebot.Id, expired: true);
        var delivery = await SeedDeliveryAsync(
            NotificationType.AngebotReady, nameof(Angebot), angebot.Id, d => d.MarkFailed(DateTime.UtcNow, "X", "y"));

        var before = await ReloadAsync(delivery.Id);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await using var context = fixture.CreateContext();
            await Assert.ThrowsAsync<ConflictException>(
                () => CreateService(context, EnabledOptions(port: 25)).RetryAsync(delivery.Id, CancellationToken.None));
        }

        var after = await ReloadAsync(delivery.Id);
        Assert.Equal(before.AttemptCount, after.AttemptCount);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.LastAttemptAt, after.LastAttemptAt);
    }

    // ---------- delivery failure is not a refusal, and is never retried on its own ----------

    [Fact]
    public async Task A_transport_failure_records_Failed_with_the_approved_sanitized_message()
    {
        var lead = await SeedLeadAsync();
        var delivery = await SeedDeliveryAsync(NotificationType.NewWebsiteLead, nameof(Lead), lead.Id);

        await using var context = fixture.CreateContext();

        // Nothing is listening on this port, so the connection is refused inside the transport phase.
        var result = await CreateService(context, EnabledOptions(port: UnusedPort()))
            .RetryAsync(delivery.Id, CancellationToken.None);

        Assert.Equal(NotificationDeliveryStatus.Failed, result.Status);
        Assert.Equal("The mail server could not be reached or rejected the message.", result.FailureMessage);
        Assert.Null(result.SentAt);
    }

    /// <summary>
    /// There is no automatic retry anywhere on this path (D69/D70): one request produces exactly one
    /// attempt, whatever its outcome. If a loop or backoff were ever added, this fails.
    /// </summary>
    [Fact]
    public async Task One_request_produces_exactly_one_attempt_even_when_it_fails()
    {
        var lead = await SeedLeadAsync();
        var delivery = await SeedDeliveryAsync(NotificationType.NewWebsiteLead, nameof(Lead), lead.Id);
        var before = (await ReloadAsync(delivery.Id)).AttemptCount;

        await using var context = fixture.CreateContext();
        await CreateService(context, EnabledOptions(port: UnusedPort())).RetryAsync(delivery.Id, CancellationToken.None);

        Assert.Equal(before + 1, (await ReloadAsync(delivery.Id)).AttemptCount);
    }

    // ---------- helpers ----------

    /// <summary>
    /// The core S5-10 assertion: a refused retry is <b>not an attempt</b>, so every field the retry
    /// path could otherwise touch must be identical afterwards. Port 25 is never reached — refusal
    /// happens before the claim and therefore before any SMTP work.
    /// </summary>
    private async Task AssertRefusesWithoutMutatingAsync(int deliveryId, string expectedReason)
    {
        var before = await ReloadAsync(deliveryId);

        await using var context = fixture.CreateContext();
        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => CreateService(context, EnabledOptions(port: 25)).RetryAsync(deliveryId, CancellationToken.None));

        Assert.Contains(expectedReason, exception.Message, StringComparison.OrdinalIgnoreCase);

        var after = await ReloadAsync(deliveryId);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.AttemptCount, after.AttemptCount);
        Assert.Equal(before.LastAttemptAt, after.LastAttemptAt);
        Assert.Equal(before.FailureType, after.FailureType);
        Assert.Equal(before.FailureMessage, after.FailureMessage);
        Assert.Equal(before.Recipient, after.Recipient);
        Assert.Equal(before.SentAt, after.SentAt);
    }

    private static EmailOptions EnabledOptions(int port, IReadOnlyList<string>? adminRecipients = null) => new()
    {
        Enabled = true,
        Host = "127.0.0.1",
        Port = port,
        SecurityMode = EmailSecurityMode.None,
        FromAddress = "no-reply@example.invalid",
        FromDisplayName = "Beispiel Bau GmbH",
        AdminRecipients = adminRecipients ?? ["office@example.invalid"],
    };

    private static int UnusedPort()
    {
        using var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    /// <summary>
    /// Composes the same two halves the container does: the service is unconditional, the executor
    /// is what only exists when delivery is enabled.
    /// </summary>
    private NotificationRetryService CreateService(RenoTrackDbContext context, EmailOptions options)
    {
        var services = new ServiceCollection();

        if (options.Enabled)
        {
            var tokenLinkOptions = new TokenLinkOptions { LifetimeDays = 30, PublicBaseUrl = "https://www.example.invalid" };
            var loggerFactory = LoggerFactory.Create(_ => { });

            services.AddSingleton(new NotificationRetryExecutor(
                context,
                new EmailMessageFactory(options, tokenLinkOptions),
                new InspectorEmailLookup(context),
                new SmtpEmailSender(
                    options,
                    new EmailMessageFactory(options, tokenLinkOptions),
                    new InspectorEmailLookup(context),
                    context,
                    loggerFactory.CreateLogger<SmtpEmailSender>())));
        }

        return new NotificationRetryService(context, options, services.BuildServiceProvider());
    }

    private async Task<NotificationDelivery> SeedDeliveryAsync(
        NotificationType type, string entityType, int entityId, Action<NotificationDelivery>? mutate = null)
    {
        var delivery = new NotificationDelivery(type, entityType, entityId);
        mutate?.Invoke(delivery);

        await using var context = fixture.CreateContext();
        context.NotificationDeliveries.Add(delivery);
        await context.SaveChangesAsync();

        return delivery;
    }

    /// <summary>
    /// Puts a row into a starting state the public API cannot reach directly (<c>Sending</c> is only
    /// ever written by the claim). Set-based, so it leaves no tracked entity behind.
    /// </summary>
    private async Task ForceStatusAsync(int deliveryId, NotificationDeliveryStatus status)
    {
        await using var context = fixture.CreateContext();
        await context.NotificationDeliveries
            .Where(d => d.Id == deliveryId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, status));
    }

    private async Task<NotificationDelivery> ReloadAsync(int deliveryId)
    {
        await using var context = fixture.CreateContext();

        return await context.NotificationDeliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);
    }

    private async Task<Lead> SeedLeadAsync()
    {
        var lead = Lead.Create($"Retry {Guid.NewGuid():N}", "+49 151 99999999", "retry@example.invalid", LeadSource.Website);

        await using var context = fixture.CreateContext();
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        return lead;
    }

    private async Task<Angebot> SeedAngebotAsync(int createdByInspectorId = 0)
    {
        var lead = await SeedLeadAsync();
        var inspectorId = createdByInspectorId == 0 ? await SeedInspectorAsync($"insp-{Guid.NewGuid():N}@example.invalid") : createdByInspectorId;
        var angebot = Angebot.Create(lead.Id, inspectionId: null, $"ANG-2026-{Random.Shared.Next(10000, 99999)}", inspectorId);

        await using var context = fixture.CreateContext();
        context.Angebote.Add(angebot);
        await context.SaveChangesAsync();

        return angebot;
    }

    private async Task<int> SeedInspectorAsync(string? email)
    {
        await using var context = fixture.CreateContext();
        var userName = email ?? $"no-address-{Guid.NewGuid():N}";
        var user = new ApplicationUser
        {
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email?.ToUpperInvariant(),
            Name = "Retry Inspector",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user.Id;
    }

    private async Task SeedReviewCommentAsync(int angebotId, string comment)
    {
        await using var context = fixture.CreateContext();
        context.AngebotReviewComments.Add(AngebotReviewComment.Create(angebotId, adminUserId: 1, comment));
        await context.SaveChangesAsync();

        // CreatedAt is DateTime.UtcNow at construction; two comments written back-to-back can share a
        // datetime2 value, which would leave "the latest" ambiguous. The Id tiebreaker resolves it,
        // but the delay makes the test's intent unambiguous rather than reliant on that.
        await Task.Delay(10);
    }

    private async Task SeedTokenLinkAsync(
        TokenLinkEntityType entityType, int entityId, bool expired = false, bool used = false)
    {
        var link = TokenLink.Create(entityType, entityId, $"tok-{Guid.NewGuid():N}", DateTime.UtcNow.AddDays(30));

        if (used)
        {
            link.MarkUsed();
        }

        await using var context = fixture.CreateContext();
        context.TokenLinks.Add(link);
        await context.SaveChangesAsync();

        if (expired)
        {
            // Create() refuses an already-past expiry (it would be useless for its whole lifetime),
            // so expiry is applied to the stored row instead of forged through the factory.
            await context.TokenLinks
                .Where(t => t.Id == link.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTime.UtcNow.AddDays(-1)));
        }
    }

    private async Task<Invoice> SeedInvoiceAsync(InvoiceStatus status)
    {
        var angebot = await SeedAngebotAsync();

        await using var context = fixture.CreateContext();

        var customer = Customer.Create(angebot.LeadId, "Kundin Test", "kundin@example.invalid", "+49 151 11111111", "Teststrasse 1");
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var project = Project.Create(customer.Id, angebot.Id, Domain.ValueObjects.Money.FromExact(1000.00m));
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var invoice = Invoice.Create(
            project.Id,
            $"RE-2026-{Random.Shared.Next(10000, 99999)}",
            DateTime.UtcNow.AddDays(14),
            Domain.ValueObjects.Money.FromExact(840.34m),
            Domain.ValueObjects.Money.FromExact(159.66m),
            Domain.ValueObjects.Money.FromExact(1000.00m));

        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        // Driven through the real transitions rather than forced, so the seeded state is one the
        // aggregate actually permits.
        invoice.Send();

        if (status == InvoiceStatus.Paid)
        {
            invoice.MarkPaid(PaymentMethod.BankTransfer, DateTime.UtcNow, recordedByAdminId: 1);
        }
        else if (status == InvoiceStatus.Void)
        {
            invoice.Void("Test void");
        }

        await context.SaveChangesAsync();

        return invoice;
    }
}
