using System.Reflection;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Domain.Tests.Entities;

public class InspectionPhotoTests
{
    /// <summary>
    /// Enforces the aggregate boundary at runtime, not just by convention: InspectionPhoto
    /// must have no public constructor, so the only way to create one from outside
    /// RenoTrack.Domain is through Inspection.AddPhoto. If someone later adds a public
    /// constructor (accidentally re-opening direct creation), this test fails immediately.
    /// </summary>
    [Fact]
    public void HasNoPublicConstructor()
    {
        var publicConstructors = typeof(InspectionPhoto).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(publicConstructors);
    }
}
