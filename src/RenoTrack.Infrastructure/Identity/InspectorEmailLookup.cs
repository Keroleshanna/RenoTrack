using Microsoft.EntityFrameworkCore;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Identity;

/// <summary>
/// Resolves an Inspector's user id to their email address, for the one notification whose recipient
/// is a specific person rather than a configured mailbox (D1).
///
/// <para><b>Why this is Infrastructure and not an <c>IUserQueries</c> method.</b> D68 committed
/// Phase 9 to changing the <c>IEmailSender</c> implementation and not its call sites.
/// <c>AngebotChangesRequestedNotification</c> carries only an <c>InspectorId</c>, and the consumer
/// that needs an address — <see cref="Email.SmtpEmailSender"/> — lives here. Adding an Application
/// interface method, widening the notification record and changing
/// <c>RequestAngebotChangesCommandHandler</c> would all be required to move this one lookup up a
/// layer, for no gain: no business rule depends on it, and Application never renders the message.</para>
///
/// <para>Queries <c>DbContext.Users</c> directly rather than through <c>UserManager</c>, matching
/// <see cref="UserQueries"/> and <see cref="TokenService"/>: this is a read-only projection to a
/// single column, and <c>UserManager</c> would materialize a whole user to answer it.</para>
/// </summary>
public sealed class InspectorEmailLookup(RenoTrackDbContext dbContext)
{
    /// <summary>
    /// The address, or <see langword="null"/> when no such user exists or the row carries no address.
    ///
    /// <para><b>No <c>IsActive</c> filter, deliberately (D3).</b> Deactivation governs whether
    /// someone may act in the dashboard — that question already has its own method,
    /// <c>IUserQueries.IsActiveInspectorAsync</c>, used when assigning work. Whether a person is told
    /// what happened to an Angebot they own is a different question, and no document makes
    /// deactivation an answer to it. Filtering here would invent a business rule and would silence a
    /// notification the Sequence Diagram requires.</para>
    ///
    /// <para><b>Null is a real possibility, not a defensive check.</b> <c>AspNetUsers.Email</c> is
    /// nullable — <c>ApplicationUserConfiguration</c> configures only the columns this project adds,
    /// leaving Identity's own base columns as the framework defines them. The caller treats null as a
    /// delivery failure (D2); this method's job is only to report it faithfully.</para>
    /// </summary>
    public Task<string?> FindEmailAsync(int inspectorId, CancellationToken cancellationToken) =>
        dbContext.Users
            .Where(user => user.Id == inspectorId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync(cancellationToken);
}
