using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Inspections.Commands.ScheduleInspection;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Inspections.Commands.ScheduleInspection;

public class ScheduleInspectionCommandHandlerTests
{
    private static readonly DateTime ScheduledAt = new(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly FakeLeadRepository _leadRepository = new();
    private readonly FakeInspectionRepository _inspectionRepository = new();
    private readonly FakeUserQueries _userQueries = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly ScheduleInspectionCommandHandler _handler;

    public ScheduleInspectionCommandHandlerTests()
    {
        _handler = new ScheduleInspectionCommandHandler(
            new ScheduleInspectionCommandValidator(),
            _leadRepository,
            _inspectionRepository,
            _userQueries,
            _unitOfWork,
            _auditService);
    }

    private Lead SeedNewLead() =>
        _leadRepository.Seed(Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Phone));

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task HandleAsync_ReturnsInspectionDtoWithSubmittedValues()
    {
        var lead = SeedNewLead();
        var command = new ScheduleInspectionCommand(lead.Id, ScheduledAt, InspectorId: 5, ScheduledByAdminId: 1);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(lead.Id, result.LeadId);
        Assert.Equal(ScheduledAt, result.ScheduledAt);
        Assert.Equal(5, result.InspectorId);
        Assert.Null(result.CompletedAt);
    }

    [Fact]
    public async Task HandleAsync_AddsInspectionToRepository()
    {
        var lead = SeedNewLead();
        var command = new ScheduleInspectionCommand(lead.Id, ScheduledAt, 5, 1);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Single(_inspectionRepository.AddedInspections);
    }

    [Fact]
    public async Task HandleAsync_AssignsInspectorToLead_BR13()
    {
        var lead = SeedNewLead();
        var command = new ScheduleInspectionCommand(lead.Id, ScheduledAt, InspectorId: 5, ScheduledByAdminId: 1);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(5, lead.AssignedInspectorId);
    }

    [Fact]
    public async Task HandleAsync_MarksLeadInspectionScheduled()
    {
        var lead = SeedNewLead();
        var command = new ScheduleInspectionCommand(lead.Id, ScheduledAt, 5, 1);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(LeadStatus.InspectionScheduled, lead.Status);
    }

    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        var lead = SeedNewLead();
        var command = new ScheduleInspectionCommand(lead.Id, ScheduledAt, 5, 1);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_LogsAuditAgainstTheLead_NotTheInspection()
    {
        var lead = SeedNewLead();
        var command = new ScheduleInspectionCommand(lead.Id, ScheduledAt, 5, ScheduledByAdminId: 9);

        await _handler.HandleAsync(command, CancellationToken.None);

        var call = Assert.Single(_auditService.Calls);
        Assert.Equal("Lead", call.EntityType);
        Assert.Equal(lead.Id, call.EntityId);
        Assert.Equal(AuditAction.InspectionScheduled, call.Action);
        Assert.Equal(9, call.PerformedByUserId);
    }

    // ---- Lead existence / status guards -----------------------------------

    [Fact]
    public async Task HandleAsync_LeadDoesNotExist_ThrowsNotFoundException()
    {
        var command = new ScheduleInspectionCommand(LeadId: 999, ScheduledAt, 5, 1);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_LeadNotInNewStatus_PropagatesDomainGuardFailure()
    {
        var lead = SeedNewLead();
        lead.MarkInspectionScheduled(); // already past New
        var command = new ScheduleInspectionCommand(lead.Id, ScheduledAt, 5, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    // ---- Validation ----------------------------------------------------

    [Theory]
    [InlineData(0, 5, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 5, 0)]
    public async Task HandleAsync_InvalidCommand_ThrowsAndPerformsNoSideEffects(int leadId, int inspectorId, int scheduledByAdminId)
    {
        var command = new ScheduleInspectionCommand(leadId, ScheduledAt, inspectorId, scheduledByAdminId);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Empty(_inspectionRepository.AddedInspections);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    // ---- Assignee eligibility (Slice 7) -----------------------------------

    [Fact]
    public async Task HandleAsync_InspectorIsNotAnActiveInspector_ThrowsAndPerformsNoSideEffects()
    {
        var lead = SeedNewLead();
        _userQueries.TreatAllAsActiveInspectors = false; // nobody is eligible

        var command = new ScheduleInspectionCommand(lead.Id, ScheduledAt, InspectorId: 5, ScheduledByAdminId: 1);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command, CancellationToken.None));

        // Rejected before anything is persisted, and before the Lead is mutated — otherwise a bad
        // assignee id would still have moved the Lead's status and assigned it in memory.
        Assert.Empty(_inspectionRepository.AddedInspections);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
        Assert.Equal(LeadStatus.New, lead.Status);
        Assert.Null(lead.AssignedInspectorId);
    }

    [Fact]
    public async Task HandleAsync_ChecksEligibilityOfTheAssignedInspectorNotTheSchedulingAdmin()
    {
        var lead = SeedNewLead();
        _userQueries.TreatAllAsActiveInspectors = false;
        _userQueries.ActiveInspectorIds.Add(5);

        var command = new ScheduleInspectionCommand(lead.Id, ScheduledAt, InspectorId: 5, ScheduledByAdminId: 1);

        await _handler.HandleAsync(command, CancellationToken.None);

        // The Admin doing the scheduling is not required to be an Inspector — confusing the two
        // would make it impossible for an Admin to schedule anyone.
        Assert.Equal([5], _userQueries.QueriedUserIds);
    }
}
