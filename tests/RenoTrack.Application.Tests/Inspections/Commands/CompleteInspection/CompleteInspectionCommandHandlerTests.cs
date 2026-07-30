using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Inspections.Commands.CompleteInspection;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Inspections.Commands.CompleteInspection;

public class CompleteInspectionCommandHandlerTests
{
    private static readonly DateTime ScheduledAt = new(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc);
    private const int AssignedInspectorId = 5;

    private readonly FakeLeadRepository _leadRepository = new();
    private readonly FakeInspectionRepository _inspectionRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly CompleteInspectionCommandHandler _handler;

    public CompleteInspectionCommandHandlerTests()
    {
        _handler = new CompleteInspectionCommandHandler(
            new CompleteInspectionCommandValidator(),
            _inspectionRepository,
            _leadRepository,
            _unitOfWork,
            _auditService,
            new OwnershipValidator()); // real instance — no I/O, nothing to fake
    }

    /// <summary>Seeds a Lead already at InspectionScheduled with a matching, seeded Inspection — the normal precondition for CompleteInspectionCommand.</summary>
    private (Lead Lead, Inspection Inspection) SeedScheduledInspection()
    {
        var lead = _leadRepository.Seed(Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Phone));
        lead.MarkInspectionScheduled();

        var inspection = _inspectionRepository.Seed(Inspection.Schedule(lead.Id, ScheduledAt, AssignedInspectorId));

        return (lead, inspection);
    }

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task HandleAsync_ReturnsInspectionDtoWithCompletedAtSet()
    {
        var (_, inspection) = SeedScheduledInspection();
        var command = new CompleteInspectionCommand(inspection.Id, AssignedInspectorId);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.NotNull(result.CompletedAt);
    }

    [Fact]
    public async Task HandleAsync_MarksLeadInspectionDone()
    {
        var (lead, inspection) = SeedScheduledInspection();
        var command = new CompleteInspectionCommand(inspection.Id, AssignedInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(LeadStatus.InspectionDone, lead.Status);
    }

    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        var (_, inspection) = SeedScheduledInspection();
        var command = new CompleteInspectionCommand(inspection.Id, AssignedInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_LogsAuditAgainstTheLead_WithInspectionDoneAction()
    {
        var (lead, inspection) = SeedScheduledInspection();
        var command = new CompleteInspectionCommand(inspection.Id, AssignedInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        var call = Assert.Single(_auditService.Calls);
        Assert.Equal("Lead", call.EntityType);
        Assert.Equal(lead.Id, call.EntityId);
        Assert.Equal(AuditAction.InspectionDone, call.Action);
        Assert.Equal(AssignedInspectorId, call.PerformedByUserId);
    }

    // ---- Not found / ownership --------------------------------------------

    [Fact]
    public async Task HandleAsync_InspectionDoesNotExist_ThrowsNotFoundException()
    {
        var command = new CompleteInspectionCommand(InspectionId: 999, AssignedInspectorId);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_LeadReferencedByInspectionDoesNotExist_ThrowsNotFoundException()
    {
        // Deliberately not seeded into the Lead repository — simulates a data-integrity gap.
        var orphanInspection = _inspectionRepository.Seed(Inspection.Schedule(leadId: 999, ScheduledAt, AssignedInspectorId));
        var command = new CompleteInspectionCommand(orphanInspection.Id, AssignedInspectorId);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WrongInspector_ThrowsForbiddenException()
    {
        var (_, inspection) = SeedScheduledInspection();
        var command = new CompleteInspectionCommand(inspection.Id, CompletedByInspectorId: 999);

        await Assert.ThrowsAsync<ForbiddenException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_AlreadyCompleted_PropagatesDomainGuardFailure()
    {
        var (_, inspection) = SeedScheduledInspection();
        inspection.Complete();
        var command = new CompleteInspectionCommand(inspection.Id, AssignedInspectorId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    // ---- Validation ----------------------------------------------------

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 0)]
    public async Task HandleAsync_InvalidCommand_ThrowsAndPerformsNoSideEffects(int inspectionId, int completedByInspectorId)
    {
        var command = new CompleteInspectionCommand(inspectionId, completedByInspectorId);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }
}
