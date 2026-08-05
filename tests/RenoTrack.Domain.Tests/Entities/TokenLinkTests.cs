using System.Reflection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Domain.Tests.Entities;

public class TokenLinkTests
{
    private const string ValidToken = "P1Ct-3xamPle_T0kenValue_43charsLongEnough00";

    private static TokenLink CreateValid(
        TokenLinkEntityType entityType = TokenLinkEntityType.Angebot,
        int entityId = 42,
        string token = ValidToken,
        TimeSpan? lifetime = null) =>
        TokenLink.Create(entityType, entityId, token, DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromDays(30)));

    // ---- Aggregate independence (Architecture §6: polymorphic, by id only) -------------------

    /// <summary>
    /// The polymorphic reference is the whole point (Architecture §6/§7.2): a compile-time
    /// reference to Angebot would make the "one table serves Angebot and Invoice" design
    /// impossible, and would be the coupling ERD.md's "no DB-level FK" note exists to describe.
    /// </summary>
    [Fact]
    public void HasNoReferenceToAngebotType() =>
        Assert.DoesNotContain(typeof(Angebot), TypeReferences(typeof(TokenLink)));

    [Fact]
    public void Angebot_HasNoReferenceToTokenLinkType() =>
        Assert.DoesNotContain(typeof(TokenLink), TypeReferences(typeof(Angebot)));

    [Fact]
    public void HasNoPublicConstructor() =>
        Assert.Empty(typeof(TokenLink).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

    /// <summary>All member types a type exposes, including generic type arguments (so a hidden <c>List&lt;T&gt;</c> field can't hide a reference).</summary>
    private static IEnumerable<Type> TypeReferences(Type type)
    {
        var memberTypes = type
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .Concat(type
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(f => f.FieldType));

        foreach (var memberType in memberTypes)
        {
            yield return memberType;
            if (memberType.IsGenericType)
            {
                foreach (var arg in memberType.GetGenericArguments())
                    yield return arg;
            }
        }
    }

    // ---- Creation ---------------------------------------------------------------------------

    [Fact]
    public void Create_SetsAllFields()
    {
        var expiresAt = DateTime.UtcNow.AddDays(30);

        var link = TokenLink.Create(TokenLinkEntityType.Angebot, entityId: 42, ValidToken, expiresAt);

        Assert.Equal(TokenLinkEntityType.Angebot, link.EntityType);
        Assert.Equal(42, link.EntityId);
        Assert.Equal(ValidToken, link.Token);
        Assert.Equal(expiresAt, link.ExpiresAt);
        Assert.NotEqual(default, link.CreatedAt);
    }

    /// <summary>A freshly issued link has never been used — BR-4's guard has nothing to trip on yet.</summary>
    [Fact]
    public void Create_LeavesUsedAtNull() =>
        Assert.Null(CreateValid().UsedAt);

    [Fact]
    public void Create_AcceptsInvoiceEntityType() =>
        Assert.Equal(TokenLinkEntityType.Invoice, CreateValid(TokenLinkEntityType.Invoice).EntityType);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutAToken_Throws(string? token) =>
        Assert.Throws<ArgumentException>(() => CreateValid(token: token!));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNonPositiveEntityId_Throws(int entityId) =>
        Assert.Throws<ArgumentException>(() => CreateValid(entityId: entityId));

    /// <summary>
    /// A link that is already dead on arrival can never serve any purpose, so it is a caller bug
    /// rather than a state the system should be able to hold.
    /// </summary>
    [Fact]
    public void Create_WithExpiryInThePast_Throws() =>
        Assert.Throws<ArgumentException>(() => CreateValid(lifetime: TimeSpan.FromDays(-1)));

    // ---- Expiry -----------------------------------------------------------------------------

    [Fact]
    public void IsExpired_BeforeExpiry_IsFalse()
    {
        var link = CreateValid(lifetime: TimeSpan.FromDays(30));

        Assert.False(link.IsExpired(DateTime.UtcNow.AddDays(29)));
    }

    [Fact]
    public void IsExpired_AfterExpiry_IsTrue()
    {
        var link = CreateValid(lifetime: TimeSpan.FromDays(30));

        Assert.True(link.IsExpired(DateTime.UtcNow.AddDays(31)));
    }

    /// <summary>
    /// The boundary is closed, not open: at exactly ExpiresAt the link is expired. Pinned because
    /// "expires at 30 days" reading as "still valid at 30 days" is the kind of off-by-one that
    /// otherwise only surfaces as a customer complaint.
    /// </summary>
    [Fact]
    public void IsExpired_ExactlyAtExpiry_IsTrue()
    {
        var link = CreateValid();

        Assert.True(link.IsExpired(link.ExpiresAt));
    }

    // ---- BR-4: single use for decisions ------------------------------------------------------

    [Fact]
    public void MarkUsed_OnAFreshLink_StampsUsedAt()
    {
        var link = CreateValid();

        link.MarkUsed();

        Assert.NotNull(link.UsedAt);
    }

    /// <summary>
    /// BR-4 itself: a forwarded or leaked link must not be able to flip a decision after the fact.
    /// </summary>
    [Fact]
    public void MarkUsed_ASecondTime_Throws()
    {
        var link = CreateValid();
        link.MarkUsed();

        var exception = Assert.Throws<InvalidOperationException>(link.MarkUsed);

        Assert.Contains("BR-4", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The first MarkUsed's timestamp is the one that stands — a rejected second attempt must not
    /// quietly re-stamp the record it refused to change.
    /// </summary>
    [Fact]
    public void MarkUsed_ASecondTime_LeavesTheOriginalTimestampIntact()
    {
        var link = CreateValid();
        link.MarkUsed();
        var firstUsedAt = link.UsedAt;

        Assert.Throws<InvalidOperationException>(link.MarkUsed);

        Assert.Equal(firstUsedAt, link.UsedAt);
    }

    /// <summary>
    /// Expiry is guarded here as well as by the Application layer: both are facts about this
    /// aggregate's own state, so CLAUDE.md §2 puts the enforcement in the aggregate rather than
    /// trusting every future caller to have checked first.
    /// </summary>
    [Fact]
    public void MarkUsed_OnAnExpiredLink_Throws()
    {
        // The shortest lifetime the constructor will accept, then waited out — the alternative
        // would be reflecting into ExpiresAt to backdate it, which CLAUDE.md §14 permits only for
        // simulating database-assigned ids, not for stepping around a guard under test.
        var link = CreateValid(lifetime: TimeSpan.FromMilliseconds(50));
        Thread.Sleep(120);

        var exception = Assert.Throws<InvalidOperationException>(link.MarkUsed);

        Assert.Contains("expired", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarkUsed_OnAnExpiredLink_LeavesUsedAtNull()
    {
        var link = CreateValid(lifetime: TimeSpan.FromMilliseconds(50));
        Thread.Sleep(120);

        Assert.Throws<InvalidOperationException>(link.MarkUsed);

        Assert.Null(link.UsedAt);
    }
}
