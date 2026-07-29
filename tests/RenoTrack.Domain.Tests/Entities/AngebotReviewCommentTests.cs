using System.Reflection;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Domain.Tests.Entities;

public class AngebotReviewCommentTests
{
    // Architecture §6: independent aggregate, related to Angebot only by id — structurally
    // confirms neither type references the other, so the Application layer must compose them
    // rather than either aggregate creating/holding the other.
    [Fact]
    public void HasNoReferenceToAngebotType() =>
        Assert.DoesNotContain(typeof(Angebot), TypeReferences(typeof(AngebotReviewComment)));

    [Fact]
    public void Angebot_HasNoReferenceToAngebotReviewCommentType() =>
        Assert.DoesNotContain(typeof(AngebotReviewComment), TypeReferences(typeof(Angebot)));

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

    [Fact]
    public void Create_SetsAllFields()
    {
        var comment = AngebotReviewComment.Create(angebotId: 7, adminUserId: 2, comment: "Please fix the VAT rate on line 3.");

        Assert.Equal(7, comment.AngebotId);
        Assert.Equal(2, comment.AdminUserId);
        Assert.Equal("Please fix the VAT rate on line 3.", comment.Comment);
    }

    [Fact]
    public void Create_TrimsComment()
    {
        var comment = AngebotReviewComment.Create(7, 2, "  needs more detail  ");

        Assert.Equal("needs more detail", comment.Comment);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsEmptyComment(string emptyComment)
    {
        Assert.Throws<ArgumentException>(() => AngebotReviewComment.Create(7, 2, emptyComment));
    }
}
