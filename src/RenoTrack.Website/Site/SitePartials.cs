using RenoTrack.Website.Content;

namespace RenoTrack.Website.Site;

/// <summary>
/// The service cards partial's input (<b>D105</b>). The heading level is the calling page's decision, because the
/// same cards sit under an <c>h2</c> on the homepage and directly under the <c>h1</c> on the overview — a shared
/// partial must never fix a level that skips one on either page.
/// </summary>
public sealed record ServiceCardsModel
{
    public ServiceCardsModel(IReadOnlyList<ServiceOptions> services, int headingLevel)
    {
        if (headingLevel is not (2 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(headingLevel), headingLevel, "Service card headings are h2 or h3.");
        }

        Services = services;
        HeadingLevel = headingLevel;
    }

    public IReadOnlyList<ServiceOptions> Services { get; }

    public int HeadingLevel { get; }
}

/// <summary>
/// The contact section partial's input (<b>D105</b>). The heading id is the calling page's, so the section's
/// <c>aria-labelledby</c> stays unique and resolvable wherever the partial is used.
/// </summary>
/// <param name="HeadingId">The id of the section's <c>h2</c>, unique on the page.</param>
/// <param name="MailSubject">A <c>mailto:</c> subject, or <c>null</c> for none.</param>
public sealed record ContactSectionModel(string HeadingId, string? MailSubject = null);

/// <summary>The hero's contact actions (<b>D105</b>).</summary>
/// <param name="MailSubject">A <c>mailto:</c> subject, or <c>null</c> for none.</param>
public sealed record HeroActionsModel(string? MailSubject = null);
