using RenoTrack.Application.Leads.Commands.CreateLead;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Common;

/// <summary>
/// Pins <b>where the Phase 9 Slice 2 failure boundary lives</b>: in Infrastructure's
/// <c>SmtpEmailSender</c>, and nowhere else.
///
/// <para>The approved design (Option A, following D50) deliberately left the six handlers untouched.
/// This test proves that from the Application side: with a sender that throws, the exception reaches
/// the caller, because no handler catches it. If someone later "helpfully" adds a
/// <c>try</c>/<c>catch</c> around a notification call — duplicating the boundary in six thin handlers,
/// which CLAUDE.md §6 forbids — this test fails.</para>
///
/// <para>It also documents the ordering that makes the design safe: the business work is already
/// committed before the notification is attempted, so a throwing sender cannot undo it.</para>
/// </summary>
public class NotificationFailureBoundaryTests
{
    private readonly FakeLeadRepository _leadRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();

    private CreateLeadCommandHandler CreateHandler(RenoTrack.Application.Common.Interfaces.IEmailSender emailSender) =>
        new(new CreateLeadCommandValidator(), _leadRepository, _unitOfWork, _auditService, emailSender);

    private static CreateLeadCommand WebsiteLead() =>
        new("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Website, "Musterstr. 1", "Wants a quote", null);

    [Fact]
    public async Task A_handler_does_not_catch_notification_failures_itself()
    {
        var handler = CreateHandler(new ThrowingEmailSender());

        await Assert.ThrowsAsync<ThrowingEmailSender.DeliveryFailedException>(
            () => handler.HandleAsync(WebsiteLead(), CancellationToken.None));
    }

    /// <summary>
    /// The business work is committed before the notification is attempted, so even an exception
    /// escaping the sender cannot roll it back. This is what makes swallowing the failure in
    /// Infrastructure sufficient rather than merely convenient.
    /// </summary>
    [Fact]
    public async Task The_business_operation_is_already_committed_when_the_notification_fails()
    {
        var handler = CreateHandler(new ThrowingEmailSender());

        await Assert.ThrowsAsync<ThrowingEmailSender.DeliveryFailedException>(
            () => handler.HandleAsync(WebsiteLead(), CancellationToken.None));

        Assert.Single(_leadRepository.AddedLeads);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    /// <summary>
    /// The established fake is unchanged by Slice 2 — it records notifications and never throws, and
    /// every existing handler test still depends on exactly that.
    /// </summary>
    [Fact]
    public async Task The_recording_fake_still_never_throws()
    {
        var emailSender = new FakeEmailSender();
        var handler = CreateHandler(emailSender);

        await handler.HandleAsync(WebsiteLead(), CancellationToken.None);

        Assert.Single(emailSender.NewWebsiteLeadNotifications);
    }
}
