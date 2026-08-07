using System.Reflection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Tests.Entities;

public class ProjectTests
{
    private const int ValidCustomerId = 7;
    private const int ValidAngebotId = 42;
    private static readonly Money ValidAgreedTotal = Money.FromExact(25_673.36m);

    private static Project CreateValid() =>
        Project.Create(ValidCustomerId, ValidAngebotId, ValidAgreedTotal);

    /// <summary>
    /// Drives a Project to the requested state through its own real transition methods only —
    /// never reflection, never a test-only setter (CLAUDE.md §14). Every state in
    /// <see cref="ProjectStatus"/> is reachable this way, which is itself worth knowing: a state
    /// this helper could not reach would be a dead state.
    /// </summary>
    private static Project InState(ProjectStatus status)
    {
        var project = CreateValid();

        switch (status)
        {
            case ProjectStatus.Active:
                break;
            case ProjectStatus.OnHold:
                project.PutOnHold();
                break;
            case ProjectStatus.Completed:
                project.Complete();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unhandled ProjectStatus.");
        }

        Assert.Equal(status, project.Status);
        return project;
    }

    // ---- Create -------------------------------------------------------

    /// <summary>StateMachine.md §4.3: <c>[*] --ConvertAngebotToProject--> Active</c>.</summary>
    [Fact]
    public void Create_InitializesStatusAsActive()
    {
        Assert.Equal(ProjectStatus.Active, CreateValid().Status);
    }

    [Fact]
    public void Create_PreservesProvidedValues()
    {
        var project = CreateValid();

        Assert.Equal(ValidCustomerId, project.CustomerId);
        Assert.Equal(ValidAngebotId, project.AngebotId);
        Assert.Equal(ValidAgreedTotal, project.AgreedTotal);
    }

    [Fact]
    public void Create_LeavesCompletedAtNull()
    {
        Assert.Null(CreateValid().CompletedAt);
    }

    [Fact]
    public void Create_SetsCreatedAt()
    {
        var before = DateTime.UtcNow;

        var project = CreateValid();

        Assert.InRange(project.CreatedAt, before, DateTime.UtcNow);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsNonPositiveCustomerId(int customerId)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Project.Create(customerId, ValidAngebotId, ValidAgreedTotal));

        Assert.Equal("customerId", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsNonPositiveAngebotId(int angebotId)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Project.Create(ValidCustomerId, angebotId, ValidAgreedTotal));

        Assert.Equal("angebotId", ex.ParamName);
    }

    [Fact]
    public void Create_RejectsNullAgreedTotal()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => Project.Create(ValidCustomerId, ValidAngebotId, null!));

        Assert.Equal("agreedTotal", ex.ParamName);
    }

    [Fact]
    public void Create_RejectsNegativeAgreedTotal()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Project.Create(ValidCustomerId, ValidAngebotId, Money.FromExact(-0.01m)));

        Assert.Equal("agreedTotal", ex.ParamName);
    }

    /// <summary>
    /// <c>Money.Zero</c> is a legal <c>Angebot.GrossTotal</c>, so refusing it here would invent a
    /// minimum-value rule no document states.
    /// </summary>
    [Fact]
    public void Create_AllowsZeroAgreedTotal()
    {
        var project = Project.Create(ValidCustomerId, ValidAngebotId, Money.Zero);

        Assert.Equal(Money.Zero, project.AgreedTotal);
    }

    // ---- PutOnHold ----------------------------------------------------

    [Fact]
    public void PutOnHold_FromActive_MovesToOnHold()
    {
        var project = InState(ProjectStatus.Active);

        project.PutOnHold();

        Assert.Equal(ProjectStatus.OnHold, project.Status);
    }

    [Theory]
    [InlineData(ProjectStatus.OnHold)]
    [InlineData(ProjectStatus.Completed)]
    public void PutOnHold_FromAnyOtherState_Throws(ProjectStatus status)
    {
        var project = InState(status);

        var ex = Assert.Throws<InvalidOperationException>(project.PutOnHold);

        Assert.Contains(status.ToString(), ex.Message);
        Assert.Contains(nameof(ProjectStatus.Active), ex.Message);
    }

    [Fact]
    public void PutOnHold_DoesNotSetCompletedAt()
    {
        var project = InState(ProjectStatus.Active);

        project.PutOnHold();

        Assert.Null(project.CompletedAt);
    }

    // ---- Resume -------------------------------------------------------

    [Fact]
    public void Resume_FromOnHold_MovesToActive()
    {
        var project = InState(ProjectStatus.OnHold);

        project.Resume();

        Assert.Equal(ProjectStatus.Active, project.Status);
    }

    [Theory]
    [InlineData(ProjectStatus.Active)]
    [InlineData(ProjectStatus.Completed)]
    public void Resume_FromAnyOtherState_Throws(ProjectStatus status)
    {
        var project = InState(status);

        var ex = Assert.Throws<InvalidOperationException>(project.Resume);

        Assert.Contains(status.ToString(), ex.Message);
        Assert.Contains(nameof(ProjectStatus.OnHold), ex.Message);
    }

    [Fact]
    public void PutOnHold_ThenResume_ReturnsToActiveAndCanRepeat()
    {
        var project = InState(ProjectStatus.Active);

        project.PutOnHold();
        project.Resume();
        project.PutOnHold();
        project.Resume();

        Assert.Equal(ProjectStatus.Active, project.Status);
    }

    // ---- Complete -----------------------------------------------------

    [Fact]
    public void Complete_FromActive_MovesToCompleted()
    {
        var project = InState(ProjectStatus.Active);

        project.Complete();

        Assert.Equal(ProjectStatus.Completed, project.Status);
    }

    [Fact]
    public void Complete_SetsCompletedAt()
    {
        var project = InState(ProjectStatus.Active);
        var before = DateTime.UtcNow;

        project.Complete();

        Assert.NotNull(project.CompletedAt);
        Assert.InRange(project.CompletedAt.Value, before, DateTime.UtcNow);
    }

    /// <summary>
    /// StateMachine.md §4.2 draws <c>Active --> Completed</c> and gives <c>OnHold</c> no path to
    /// <c>Completed</c> — a paused Project is resumed first. Encoding the documented shape rather
    /// than the more permissive one is deliberate; a direct <c>OnHold → Completed</c> move would
    /// be a new transition, not an implementation detail.
    /// </summary>
    [Theory]
    [InlineData(ProjectStatus.OnHold)]
    [InlineData(ProjectStatus.Completed)]
    public void Complete_FromAnyOtherState_Throws(ProjectStatus status)
    {
        var project = InState(status);

        var ex = Assert.Throws<InvalidOperationException>(project.Complete);

        Assert.Contains(status.ToString(), ex.Message);
        Assert.Contains(nameof(ProjectStatus.Active), ex.Message);
    }

    /// <summary>
    /// <c>Completed</c> has no outgoing transition in StateMachine.md §4.2, so it is terminal in
    /// every direction — not merely un-completable a second time.
    /// </summary>
    [Fact]
    public void Completed_IsTerminalForEveryTransition()
    {
        var project = InState(ProjectStatus.Completed);

        Assert.Throws<InvalidOperationException>(project.PutOnHold);
        Assert.Throws<InvalidOperationException>(project.Resume);
        Assert.Throws<InvalidOperationException>(project.Complete);

        Assert.Equal(ProjectStatus.Completed, project.Status);
    }

    [Fact]
    public void FailedTransition_LeavesCompletedAtUntouched()
    {
        var project = InState(ProjectStatus.OnHold);

        Assert.Throws<InvalidOperationException>(project.Complete);

        Assert.Null(project.CompletedAt);
    }

    // ---- AgreedTotal snapshot ----------------------------------------

    /// <summary>
    /// ERD.md: "AgreedTotal is a snapshot of Angebot.GrossTotal at conversion time". No transition
    /// may move it, which is what makes the snapshot a guarantee rather than a convention.
    /// </summary>
    [Fact]
    public void AgreedTotal_SurvivesEveryTransition()
    {
        var project = CreateValid();

        project.PutOnHold();
        Assert.Equal(ValidAgreedTotal, project.AgreedTotal);

        project.Resume();
        Assert.Equal(ValidAgreedTotal, project.AgreedTotal);

        project.Complete();
        Assert.Equal(ValidAgreedTotal, project.AgreedTotal);
    }

    // ---- Structure ----------------------------------------------------

    [Fact]
    public void HasNoPublicConstructor()
    {
        var publicConstructors = typeof(Project)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(publicConstructors);
    }

    /// <summary>
    /// CLAUDE.md §2: independent aggregates relate by id only. This is the structural half of
    /// BR-2's guard living in the Application layer — <see cref="Project"/> cannot check the
    /// Angebot's status because it cannot see an Angebot at all, by construction.
    /// </summary>
    [Theory]
    [InlineData(typeof(Angebot))]
    [InlineData(typeof(Customer))]
    [InlineData(typeof(Lead))]
    public void HasNoReferenceToOtherAggregatesAsTypes(Type foreignAggregate)
    {
        var referencedTypes = typeof(Project)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .Concat(typeof(Project)
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(f => f.FieldType))
            .SelectMany(t => new[] { t }.Concat(t.GenericTypeArguments));

        Assert.DoesNotContain(foreignAggregate, referencedTypes);
    }

    [Fact]
    public void ExposesNoPublicSetters()
    {
        var settableProperties = typeof(Project)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .ToArray();

        Assert.Empty(settableProperties);
    }

    /// <summary>
    /// Every state change is a named transition (CLAUDE.md §2) — there is no public
    /// <c>SetStatus</c>-shaped escape hatch, and the mutating surface is exactly the three
    /// transitions StateMachine.md §4.3 defines.
    /// </summary>
    [Fact]
    public void ExposesExactlyTheDocumentedTransitions()
    {
        var publicMethods = typeof(Project)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(
            [nameof(Project.Complete), nameof(Project.PutOnHold), nameof(Project.Resume)],
            publicMethods);
    }
}
