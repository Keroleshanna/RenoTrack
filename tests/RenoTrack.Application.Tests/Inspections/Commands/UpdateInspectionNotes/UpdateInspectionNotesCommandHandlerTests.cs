using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Inspections.Commands.UpdateInspectionNotes;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Inspections.Commands.UpdateInspectionNotes;

public class UpdateInspectionNotesCommandHandlerTests
{
    private static readonly DateTime ScheduledAt = new(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc);
    private const int AssignedInspectorId = 5;

    private readonly FakeInspectionRepository _inspectionRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly UpdateInspectionNotesCommandHandler _handler;

    public UpdateInspectionNotesCommandHandlerTests()
    {
        _handler = new UpdateInspectionNotesCommandHandler(
            new UpdateInspectionNotesCommandValidator(),
            _inspectionRepository,
            _unitOfWork,
            new OwnershipValidator());
    }

    private Inspection SeedInspection() =>
        _inspectionRepository.Seed(Inspection.Schedule(leadId: 1, ScheduledAt, AssignedInspectorId));

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task HandleAsync_UpdatesNotes()
    {
        var inspection = SeedInspection();
        var command = new UpdateInspectionNotesCommand(inspection.Id, "Re-tile bathroom, ~10m2", AssignedInspectorId);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal("Re-tile bathroom, ~10m2", result.Notes);
        Assert.Equal("Re-tile bathroom, ~10m2", inspection.Notes);
    }

    [Fact]
    public async Task HandleAsync_AllowsClearingNotesToNull()
    {
        var inspection = SeedInspection();
        inspection.UpdateNotes("first draft");
        var command = new UpdateInspectionNotesCommand(inspection.Id, null, AssignedInspectorId);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Null(result.Notes);
    }

    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        var inspection = SeedInspection();
        var command = new UpdateInspectionNotesCommand(inspection.Id, "Some notes", AssignedInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    // ---- Not found / ownership / BR-10 --------------------------------

    [Fact]
    public async Task HandleAsync_InspectionDoesNotExist_ThrowsNotFoundException()
    {
        var command = new UpdateInspectionNotesCommand(InspectionId: 999, "Some notes", AssignedInspectorId);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WrongInspector_ThrowsForbiddenException()
    {
        var inspection = SeedInspection();
        var command = new UpdateInspectionNotesCommand(inspection.Id, "Some notes", UpdatedByInspectorId: 999);

        await Assert.ThrowsAsync<ForbiddenException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_InspectionAlreadyCompleted_ThrowsAndDoesNotChangeNotes_BR10()
    {
        var inspection = SeedInspection();
        inspection.UpdateNotes("original notes");
        inspection.Complete();
        var command = new UpdateInspectionNotesCommand(inspection.Id, "trying to sneak in a change", AssignedInspectorId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Equal("original notes", inspection.Notes);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    // ---- Validation ----------------------------------------------------

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 0)]
    public async Task HandleAsync_InvalidIds_ThrowsValidationException(int inspectionId, int updatedByInspectorId)
    {
        var command = new UpdateInspectionNotesCommand(inspectionId, "Some notes", updatedByInspectorId);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }
}
