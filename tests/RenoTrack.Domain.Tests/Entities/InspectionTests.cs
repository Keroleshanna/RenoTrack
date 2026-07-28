using RenoTrack.Domain.Entities;

namespace RenoTrack.Domain.Tests.Entities;

public class InspectionTests
{
    private static readonly DateTime ScheduledAt = new(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc);

    // ---- Schedule -------------------------------------------------------

    [Fact]
    public void Schedule_InitializesFieldsCorrectly()
    {
        var inspection = Inspection.Schedule(leadId: 7, ScheduledAt, inspectorId: 3);

        Assert.Equal(7, inspection.LeadId);
        Assert.Equal(ScheduledAt, inspection.ScheduledAt);
        Assert.Equal(3, inspection.InspectorId);
    }

    [Fact]
    public void Schedule_SetsCompletedAtToNull()
    {
        var inspection = Inspection.Schedule(7, ScheduledAt, 3);

        Assert.Null(inspection.CompletedAt);
    }

    [Fact]
    public void Schedule_StartsWithNullNotes()
    {
        var inspection = Inspection.Schedule(7, ScheduledAt, 3);

        Assert.Null(inspection.Notes);
    }

    [Fact]
    public void Schedule_StartsWithNoPhotos()
    {
        var inspection = Inspection.Schedule(7, ScheduledAt, 3);

        Assert.Empty(inspection.Photos);
    }

    // ---- AddPhoto -------------------------------------------------------

    [Fact]
    public void AddPhoto_AddsToPhotosCollection()
    {
        var inspection = Inspection.Schedule(7, ScheduledAt, 3);

        inspection.AddPhoto("https://storage.local/inspections/1/a.jpg", "Bathroom floor");

        var photo = Assert.Single(inspection.Photos);
        Assert.Equal("https://storage.local/inspections/1/a.jpg", photo.FileUrl);
        Assert.Equal("Bathroom floor", photo.Caption);
    }

    [Fact]
    public void AddPhoto_AllowsMultiplePhotos()
    {
        var inspection = Inspection.Schedule(7, ScheduledAt, 3);

        inspection.AddPhoto("https://storage.local/inspections/1/a.jpg");
        inspection.AddPhoto("https://storage.local/inspections/1/b.jpg");

        Assert.Equal(2, inspection.Photos.Count);
    }

    [Fact]
    public void AddPhoto_AllowsNullCaption()
    {
        var inspection = Inspection.Schedule(7, ScheduledAt, 3);

        inspection.AddPhoto("https://storage.local/inspections/1/a.jpg");

        Assert.Null(inspection.Photos[0].Caption);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddPhoto_RejectsEmptyFileUrl(string emptyUrl)
    {
        var inspection = Inspection.Schedule(7, ScheduledAt, 3);

        Assert.Throws<ArgumentException>(() => inspection.AddPhoto(emptyUrl));
    }

    // ---- UpdateNotes ------------------------------------------------------

    [Fact]
    public void UpdateNotes_SetsNotes()
    {
        var inspection = Inspection.Schedule(7, ScheduledAt, 3);

        inspection.UpdateNotes("Re-tile bathroom floor, ~10m2");

        Assert.Equal("Re-tile bathroom floor, ~10m2", inspection.Notes);
    }

    [Fact]
    public void UpdateNotes_TrimsWhitespace()
    {
        var inspection = Inspection.Schedule(7, ScheduledAt, 3);

        inspection.UpdateNotes("  needs work  ");

        Assert.Equal("needs work", inspection.Notes);
    }

    [Fact]
    public void UpdateNotes_AllowsClearingToNull()
    {
        var inspection = Inspection.Schedule(7, ScheduledAt, 3);
        inspection.UpdateNotes("first draft");

        inspection.UpdateNotes(null);

        Assert.Null(inspection.Notes);
    }

    // ---- Complete -------------------------------------------------------

    [Fact]
    public void Complete_SetsCompletedAt()
    {
        var inspection = Inspection.Schedule(7, ScheduledAt, 3);

        inspection.Complete();

        Assert.NotNull(inspection.CompletedAt);
    }

    [Fact]
    public void Complete_ThrowsWhenAlreadyCompleted()
    {
        var inspection = Inspection.Schedule(7, ScheduledAt, 3);
        inspection.Complete();

        Assert.Throws<InvalidOperationException>(() => inspection.Complete());
    }

    // ---- BR-10: completed Inspections are immutable ----------------------

    [Fact]
    public void AddPhoto_ThrowsAfterCompletion_BR10()
    {
        var inspection = Inspection.Schedule(7, ScheduledAt, 3);
        inspection.Complete();

        var exception = Assert.Throws<InvalidOperationException>(
            () => inspection.AddPhoto("https://storage.local/inspections/1/late.jpg"));
        Assert.Contains("BR-10", exception.Message);
    }

    [Fact]
    public void UpdateNotes_ThrowsAfterCompletion_BR10()
    {
        var inspection = Inspection.Schedule(7, ScheduledAt, 3);
        inspection.Complete();

        var exception = Assert.Throws<InvalidOperationException>(
            () => inspection.UpdateNotes("trying to sneak in a change"));
        Assert.Contains("BR-10", exception.Message);
    }
}
