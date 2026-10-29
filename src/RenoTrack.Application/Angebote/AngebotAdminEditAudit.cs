namespace RenoTrack.Application.Angebote;

/// <summary>The five builder mutations an Admin edit audit row can record (D115).</summary>
public enum AngebotEditOperation
{
    SectionAdded,
    SectionRemoved,
    ItemAdded,
    ItemUpdated,
    ItemRemoved,
}

/// <summary>
/// The <c>details</c> text of an <c>AngebotEditedByAdmin</c> audit row (<b>D115</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Which part of the document was touched, and nothing else.</b> The Angebot itself is the audit
/// row's <c>EntityId</c>; the details add only the operation and the section/item association. No
/// amount, no description or specification text, no customer data — the Product Owner's approved
/// boundary. Whoever wants to know <i>what</i> a line now says reads the Angebot.
/// </para>
/// <para>
/// One formatter so every handler writes the same shape: <c>operation=ItemUpdated; sectionId=12;
/// itemId=40</c>. The ids of a removed section or item name rows that no longer exist; that is the
/// honest record of what was removed.
/// </para>
/// </remarks>
public static class AngebotAdminEditAudit
{
    public static string Details(AngebotEditOperation operation, int sectionId, int? itemId = null) =>
        itemId is { } id
            ? $"operation={operation}; sectionId={sectionId}; itemId={id}"
            : $"operation={operation}; sectionId={sectionId}";
}
