using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using RenoTrack.Application.Common;
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
            _unitOfWork,
            new OwnershipValidator(),
            NullLogger<UploadInspectionPhotoCommandHandler>.Instance);
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

    // ---- Extension validation (Slice 8) ---------------------------------

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.JPEG")]
    [InlineData("photo.heic")]
    [InlineData("photo.avif")]
    [InlineData("photo.cr2")]
    [InlineData("photo")]              // no extension at all is fine — the key simply has none
    [InlineData("photo.")]             // a trailing dot yields "" from GetExtension, not "." — so also no extension
    [InlineData("../../evil.jpg")]     // traversal is neutralised by GetExtension, so this is valid
    public async Task HandleAsync_UsableExtension_IsAccepted(string fileName)
    {
        var inspection = SeedInspection();
        var command = ValidCommand(inspection.Id) with { FileName = fileName };

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Whatever the caller sent, the stored key is built from controlled components only.
        Assert.StartsWith($"inspections/{inspection.Id}/", result.FileUrl);
        Assert.DoesNotContain("..", result.FileUrl);
        Assert.DoesNotContain("evil", result.FileUrl);
    }

    [Theory]
    [InlineData("photo.jp g")]     // space
    [InlineData("photo.jp*g")]     // Windows-invalid, but rejected on every platform
    [InlineData("photo.jp?g")]
    [InlineData("photo.jp|g")]
    [InlineData("photo.jp:g")]
    public async Task HandleAsync_UnusableExtension_IsRejectedBeforeAnyIo(string fileName)
    {
        var inspection = SeedInspection();
        var command = ValidCommand(inspection.Id) with { FileName = fileName };

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Empty(_fileStorage.SavedFiles);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_OverlongExtension_IsRejectedRatherThanReachingTheFilesystem()
    {
        var inspection = SeedInspection();
        var command = ValidCommand(inspection.Id) with
        {
            FileName = "photo." + new string('x', UploadInspectionPhotoCommandValidator.MaxFileExtensionLength),
        };

        // Measured, not hypothetical: unbounded, this composed a path long enough for File.Create
        // to throw IOException — unmapped by the ProblemDetails middleware, so a 500.
        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Empty(_fileStorage.SavedFiles);
    }

    [Fact]
    public async Task HandleAsync_ExtensionAtTheMaximumLength_IsAccepted()
    {
        var inspection = SeedInspection();
        var command = ValidCommand(inspection.Id) with
        {
            FileName = "photo." + new string('x', UploadInspectionPhotoCommandValidator.MaxFileExtensionLength - 1),
        };

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Single(_fileStorage.SavedFiles);
    }

    // ---- Consistency between storage and the database (Slice 8) ---------

    [Fact]
    public async Task HandleAsync_FileWriteFails_NothingIsCommitted()
    {
        var inspection = SeedInspection();
        _fileStorage.SaveFailure = new IOException("disk unavailable");

        await Assert.ThrowsAsync<IOException>(
            () => _handler.HandleAsync(ValidCommand(inspection.Id), CancellationToken.None));

        // The aggregate was mutated in memory before the write, but the commit never happens, so
        // the change dies with the request-scoped DbContext.
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_CommitFails_TheWrittenFileIsRemoved()
    {
        var inspection = SeedInspection();
        _unitOfWork.SaveFailure = new InvalidOperationException("commit failed");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(ValidCommand(inspection.Id), CancellationToken.None));

        // The original database failure reaches the caller, not a cleanup error.
        Assert.Equal("commit failed", thrown.Message);

        // And the file that would otherwise have been orphaned is gone. Asserting on the storage's
        // contents rather than on "delete was called" is what makes this meaningful.
        Assert.Empty(_fileStorage.SavedFiles);
        Assert.Single(_fileStorage.DeletedFileUrls);
    }

    [Fact]
    public async Task HandleAsync_CommitFailsAndCompensationAlsoFails_TheOriginalFailureStillSurfaces()
    {
        var inspection = SeedInspection();
        _unitOfWork.SaveFailure = new InvalidOperationException("commit failed");
        _fileStorage.DeleteFailure = new IOException("delete failed");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(ValidCommand(inspection.Id), CancellationToken.None));

        // A failed cleanup must never replace the real reason the request failed — otherwise the
        // caller is told something misleading about what went wrong.
        Assert.Equal("commit failed", thrown.Message);

        // Compensation was attempted, and the orphan genuinely remains: this is compensation, not
        // atomicity, and the test says so rather than pretending otherwise.
        Assert.Single(_fileStorage.DeletedFileUrls);
        Assert.Single(_fileStorage.SavedFiles);
    }
}
