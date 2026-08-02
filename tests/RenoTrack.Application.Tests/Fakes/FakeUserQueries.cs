using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// Hand-written fake, no mocking framework (CLAUDE.md §14).
/// </summary>
/// <remarks>
/// Defaults to treating every id as an active Inspector so existing tests, which care about
/// scheduling behaviour rather than assignee eligibility, stay readable. Tests that exercise the
/// eligibility rule seed <see cref="ActiveInspectorIds"/> explicitly and set
/// <see cref="TreatAllAsActiveInspectors"/> to <c>false</c> — an opt-in switch rather than a default,
/// so the restrictive path is always deliberate.
/// </remarks>
public sealed class FakeUserQueries : IUserQueries
{
    public bool TreatAllAsActiveInspectors { get; set; } = true;

    public HashSet<int> ActiveInspectorIds { get; } = [];

    public List<int> QueriedUserIds { get; } = [];

    public Task<bool> IsActiveInspectorAsync(int userId, CancellationToken cancellationToken)
    {
        QueriedUserIds.Add(userId);

        return Task.FromResult(TreatAllAsActiveInspectors || ActiveInspectorIds.Contains(userId));
    }
}
