using FluentValidation;
using RenoTrack.Application.Angebote.Commands.CreateAngebot;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Angebote.Commands.CreateAngebot;

public class CreateAngebotCommandHandlerTests
{
    private const int AssignedInspectorId = 5;

    private readonly FakeLeadRepository _leadRepository = new();
    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly FakeNumberGeneratorService _numberGenerator = new();
    private readonly OwnershipValidator _ownershipValidator = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly CreateAngebotCommandHandler _handler;

    public CreateAngebotCommandHandlerTests()
    {
        _handler = new CreateAngebotCommandHandler(
            new CreateAngebotCommandValidator(),
            _leadRepository,
            _angebotRepository,
            _numberGenerator,
            _ownershipValidator,
            _unitOfWork,
            _auditService);
    }

    /// <summary>Seeds a Lead already past InspectionDone with an assigned Inspector — the normal precondition for CreateAngebotCommand.</summary>
    private Lead SeedLeadReadyForAngebot(int inspectorId = AssignedInspectorId)
    {
        var lead = _leadRepository.Seed(Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Phone));
        lead.MarkInspectionScheduled();
        lead.AssignInspector(inspectorId);
        lead.MarkInspectionDone();
        return lead;
    }

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task HandleAsync_ReturnsAngebotDtoWithDraftStatus()
    {
        var lead = SeedLeadReadyForAngebot();
        var command = new CreateAngebotCommand(lead.Id, InspectionId: null, AssignedInspectorId);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(AngebotStatus.Draft, result.Status);
        Assert.Equal(lead.Id, result.LeadId);
    }

    [Fact]
    public async Task HandleAsync_UsesTheGeneratedAngebotNumber()
    {
        var lead = SeedLeadReadyForAngebot();
        _numberGenerator.NextAngebotNumber = "ANG-2026-00042";
        var command = new CreateAngebotCommand(lead.Id, null, AssignedInspectorId);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal("ANG-2026-00042", result.AngebotNumber);
    }

    [Fact]
    public async Task HandleAsync_RequestsTheNumberForTheCurrentYear()
    {
        var lead = SeedLeadReadyForAngebot();
        var command = new CreateAngebotCommand(lead.Id, null, AssignedInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        var year = Assert.Single(_numberGenerator.RequestedYears);
        Assert.Equal(DateTime.UtcNow.Year, year);
    }

    [Fact]
    public async Task HandleAsync_AddsAngebotToRepository()
    {
        var lead = SeedLeadReadyForAngebot();
        var command = new CreateAngebotCommand(lead.Id, null, AssignedInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Single(_angebotRepository.AddedAngebote);
    }

    [Fact]
    public async Task HandleAsync_MarksLeadAngebotInProgress()
    {
        var lead = SeedLeadReadyForAngebot();
        var command = new CreateAngebotCommand(lead.Id, null, AssignedInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(LeadStatus.AngebotInProgress, lead.Status);
    }

    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        var lead = SeedLeadReadyForAngebot();
        var command = new CreateAngebotCommand(lead.Id, null, AssignedInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_LogsAuditAgainstTheLead_WithAngebotCreatedAction()
    {
        var lead = SeedLeadReadyForAngebot();
        var command = new CreateAngebotCommand(lead.Id, null, AssignedInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        var call = Assert.Single(_auditService.Calls);
        Assert.Equal("Lead", call.EntityType);
        Assert.Equal(lead.Id, call.EntityId);
        Assert.Equal(AuditAction.AngebotCreated, call.Action);
        Assert.Equal(AssignedInspectorId, call.PerformedByUserId);
    }

    // ---- Not found / ownership / conflict ---------------------------------

    [Fact]
    public async Task HandleAsync_LeadDoesNotExist_ThrowsNotFoundException()
    {
        var command = new CreateAngebotCommand(LeadId: 999, null, AssignedInspectorId);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_InspectorNotAssignedToLead_ThrowsForbiddenException()
    {
        var lead = SeedLeadReadyForAngebot(inspectorId: AssignedInspectorId);
        var command = new CreateAngebotCommand(lead.Id, null, CreatedByInspectorId: 999);

        await Assert.ThrowsAsync<ForbiddenException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_LeadAlreadyHasActiveAngebot_ThrowsConflictException()
    {
        var lead = SeedLeadReadyForAngebot();
        _angebotRepository.HasActiveAngebotForLead = true;
        var command = new CreateAngebotCommand(lead.Id, null, AssignedInspectorId);

        await Assert.ThrowsAsync<ConflictException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_LeadNotInspectionDone_PropagatesDomainGuardFailure()
    {
        // Lead still New — never scheduled/completed an Inspection.
        var lead = _leadRepository.Seed(Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Phone));
        lead.AssignInspector(AssignedInspectorId);
        var command = new CreateAngebotCommand(lead.Id, null, AssignedInspectorId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    // ---- Validation ----------------------------------------------------

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 0)]
    public async Task HandleAsync_InvalidCommand_ThrowsAndPerformsNoSideEffects(int leadId, int createdByInspectorId)
    {
        var command = new CreateAngebotCommand(leadId, null, createdByInspectorId);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Empty(_angebotRepository.AddedAngebote);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }
}
