namespace RenoTrack.Domain.Enums;

/// <summary>
/// How a Lead first reached the company. A closed set — SRS.md §4.1/FR-2.1 and ERD.md both
/// name exactly these three channels, with no "etc." (unlike Unit/ItemUnit), so a plain
/// closed enum is the right fit here, not the open Value Object pattern.
/// </summary>
public enum LeadSource
{
    /// <summary>Public website contact form. SRS FR-1.3.</summary>
    Website,

    /// <summary>Logged manually by Admin after a phone call. SRS FR-2.1.</summary>
    Phone,

    /// <summary>Logged manually by Admin after an email exchange. SRS FR-2.1.</summary>
    Email
}
