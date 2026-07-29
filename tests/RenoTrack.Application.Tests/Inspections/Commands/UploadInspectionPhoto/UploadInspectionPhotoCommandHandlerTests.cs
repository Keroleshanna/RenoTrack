using FluentValidation;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Inspections.Commands.UploadInspectionPhoto;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Inspections.Commands.UploadInspectionPhoto;

public class UploadInspectionPhotoCommandHandlerTests
{
    private static readonly DateTime ScheduledAt = new(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc);
    private const int AssignedInspectorId = 5;

    private readonly FakeInspectionRepository _inspectionRepository = new();
    private readonly FakeFileStorage _fileStorage = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly UploadInspectionPhotoCommandHandler _handler;

    public UploadInspectionPhotoCommandHandlerTests()
    {
        _handler = new UploadInspectionPhotoCommandHandler(
            new UploadInspectionPhotoCommandValidator(),
            _inspectionRepository,
            _fileStorage,
            _unitOfWork);
    }

    private Inspection SeedInspection() =>
        _inspectionRepository.Seed(Inspection.Schedule(leadId: 1, ScheduledAt, AssignedInspectorId));

    private static UploadInspectionPhotoCommand ValidCommand(int inspectionId, int uploadedByInspectorId = AssignedInspectorId, string? caption = "Bathroom floor") =>
        new(inspectionId, new MemoryStream([1, 2, 3, 4]), "photo.jpg", caption, uploadedByInspectorId);

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task HandleAsync_ReturnsPhotoDtoWithAGeneratedFileUrl()
    {
        var inspection = SeedInspection();

        var result = await _handler.HandleAsync(ValidCommand(inspection.Id), CancellationToken.None);

        Assert.StartsWith($"inspections/{inspection.Id}/", result.FileUrl);
        Assert.EndsWith(".jpg", result.FileUrl);
        Assert.Equal("Bathroom floor", result.Caption);
    }

    [Fact]
    public async Task HandleAsync_AddsPhotoToInspection()
    {
        var inspection = SeedInspection();

        await _handler.HandleAsync(ValidCommand(inspection.Id), CancellationToken.None);

        Assert.Single(inspection.Photos);
    }

    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        var inspection = SeedInspection();

        await _handler.HandleAsync(ValidCommand(inspection.Id), CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_SavesToStorageWithTheSameFileUrlUsedForThePhoto()
    {
        var inspection = SeedInspection();

        var result = await _handler.HandleAsync(ValidCommand(inspection.Id), CancellationToken.None);

        var saved = Assert.Single(_fileStorage.SavedFiles);
        Assert.Equal(result.FileUrl, saved.FileUrl);
        Assert.Equal(4, saved.ContentLength);
    }

    // No AuditLog assertion is needed: the handler doesn't even take an IAuditService
    // dependency, so logging is structurally impossible here, not just unobserved.

    // ---- Not found / ownership / BR-10 --------------------------------

    [Fact]
    public async Task HandleAsync_InspectionDoesNotExist_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(ValidCommand(inspectionId: 999), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WrongInspector_ThrowsForbiddenException()
    {
        var inspection = SeedInspection();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _handler.HandleAsync(ValidCommand(inspection.Id, uploadedByInspectorId: 999), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_InspectionAlreadyCompleted_ThrowsAndNeverTouchesFileStorage_BR10()
    {
        var inspection = SeedInspection();
        inspection.Complete();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(ValidCommand(inspection.Id), CancellationToken.None));

        // The whole point of computing FileUrl before calling AddPhoto: BR-10's rejection
        // happens before any I/O, so no orphaned file is ever written to storage.
        Assert.Empty(_fileStorage.SavedFiles);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    // ---- Validation ----------------------------------------------------

    [Fact]
    public async Task HandleAsync_EmptyFileName_ThrowsAndPerformsNoSideEffects()
    {
        var inspection = SeedInspection();
        var command = ValidCommand(inspection.Id) with { FileName = "" };

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Empty(_fileStorage.SavedFiles);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 0)]
    public async Task HandleAsync_InvalidIds_ThrowsValidationException(int inspectionId, int uploadedByInspectorId)
    {
        var command = ValidCommand(inspectionId, uploadedByInspectorId);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }
}
