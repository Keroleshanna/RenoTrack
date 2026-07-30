using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Common;

public class OwnershipValidatorTests
{
    private readonly OwnershipValidator _validator = new();

    [Fact]
    public void EnsureInspectionOwnership_DoesNothing_WhenInspectorMatches()
    {
        var inspection = Inspection.Schedule(leadId: 1, DateTime.UtcNow, inspectorId: 5);

        var exception = Record.Exception(() => _validator.EnsureInspectionOwnership(inspection, 5));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureInspectionOwnership_Throws_WhenInspectorDoesNotMatch()
    {
        var inspection = Inspection.Schedule(leadId: 1, DateTime.UtcNow, inspectorId: 5);

        Assert.Throws<ForbiddenException>(() => _validator.EnsureInspectionOwnership(inspection, 999));
    }
}
