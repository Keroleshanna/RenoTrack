using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Leads.Commands.CreateLead;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Leads.Commands.CreateLead;

public class CreateLeadCommandHandlerTests
{
    private readonly FakeLeadRepository _leadRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly FakeEmailSender _emailSender = new();
    private readonly CreateLeadCommandHandler _handler;

    public CreateLeadCommandHandlerTests()
    {
        _handler = new CreateLeadCommandHandler(
            new CreateLeadCommandValidator(),
            _leadRepository,
            _unitOfWork,
            _auditService,
            _emailSender);
    }

    private static CreateLeadCommand ValidCommand(LeadSource source = LeadSource.Website, int? createdByUserId = null) =>
        new("Jane Doe", "0176 1234567", "jane@example.com", source, "Musterstr. 1", "Wants a quote", createdByUserId);

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task HandleAsync_ReturnsLeadDtoWithSubmittedValues()
    {
        var result = await _handler.HandleAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal("Jane Doe", result.Name);
        Assert.Equal("0176 1234567", result.Phone);
        Assert.Equal("jane@example.com", result.Email);
        Assert.Equal(LeadSource.Website, result.Source);
        Assert.Equal(LeadStatus.New, result.Status);
    }

    [Fact]
    public async Task HandleAsync_AddsLeadToRepository()
    {
        await _handler.HandleAsync(ValidCommand(), CancellationToken.None);

        Assert.Single(_leadRepository.AddedLeads);
    }

    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        await _handler.HandleAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_LogsLeadCreatedAudit()
    {
        var result = await _handler.HandleAsync(ValidCommand(source: LeadSource.Phone, createdByUserId: 9), CancellationToken.None);

        var call = Assert.Single(_auditService.Calls);
        Assert.Equal("Lead", call.EntityType);
        Assert.Equal(result.Id, call.EntityId);
        Assert.Equal(AuditAction.LeadCreated, call.Action);
        Assert.Equal(9, call.PerformedByUserId);
    }

    // ---- Notification dispatch (SRS FR-9.2: website Leads only) -----------

    [Fact]
    public async Task HandleAsync_WebsiteSource_SendsNewWebsiteLeadNotification()
    {
        var result = await _handler.HandleAsync(ValidCommand(source: LeadSource.Website), CancellationToken.None);

        var notification = Assert.Single(_emailSender.NewWebsiteLeadNotifications);
        Assert.Equal(result.Id, notification.LeadId);
        Assert.Equal("Jane Doe", notification.LeadName);
        Assert.Equal("0176 1234567", notification.LeadPhone);
        Assert.Equal("jane@example.com", notification.LeadEmail);
    }

    [Theory]
    [InlineData(LeadSource.Phone)]
    [InlineData(LeadSource.Email)]
    public async Task HandleAsync_NonWebsiteSource_DoesNotSendNotification(LeadSource source)
    {
        await _handler.HandleAsync(ValidCommand(source: source), CancellationToken.None);

        Assert.Empty(_emailSender.NewWebsiteLeadNotifications);
    }

    // ---- Validation ----------------------------------------------------

    [Fact]
    public async Task HandleAsync_InvalidCommand_ThrowsAndPerformsNoSideEffects()
    {
        var invalidCommand = ValidCommand() with { Name = "" };

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(invalidCommand, CancellationToken.None));

        Assert.Empty(_leadRepository.AddedLeads);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
        Assert.Empty(_emailSender.NewWebsiteLeadNotifications);
    }
}
