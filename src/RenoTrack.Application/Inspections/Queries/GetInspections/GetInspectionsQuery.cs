using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Inspections.Dtos;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Inspections.Queries.GetInspections;

// -------------------------------------------------------------------------------------------------
// One Inspection
// -------------------------------------------------------------------------------------------------

/// <param name="RequestingInspectorId">
/// The caller's own id when they are an Inspector, <see langword="null"/> for an Admin. Set from the
/// token by the API layer, never from the request (D61).
/// </param>
public sealed record GetInspectionByIdQuery(int Id, int? RequestingInspectorId);

public sealed class GetInspectionByIdQueryValidator : AbstractValidator<GetInspectionByIdQuery>
{
    public GetInspectionByIdQueryValidator()
    {
        RuleFor(q => q.Id).GreaterThan(0);
    }
}

/// <remarks>
/// <b>A non-owning Inspector gets 404, not 403.</b> The scope is a <c>WHERE</c> clause, so the query
/// simply finds nothing — and that is the right answer to surface: distinguishing "does not exist"
/// from "exists but is not yours" would tell an Inspector that another Inspector has an assignment
/// with that id. An Admin is unscoped, so they get the real 404 only when the row truly is absent.
/// </remarks>
public sealed class GetInspectionByIdQueryHandler(
    IValidator<GetInspectionByIdQuery> validator,
    IInspectionQueries inspectionQueries) : IQueryHandler<GetInspectionByIdQuery, InspectionDetailDto>
{
    public async Task<InspectionDetailDto> HandleAsync(
        GetInspectionByIdQuery query,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        return await inspectionQueries.GetByIdAsync(query.Id, query.RequestingInspectorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Inspection), query.Id);
    }
}

// -------------------------------------------------------------------------------------------------
// The schedule
// -------------------------------------------------------------------------------------------------

/// <summary>
/// Site visits in a time window — the Cockpit's day plan and the Besichtigungen workspace.
/// </summary>
/// <param name="To">Exclusive, so <c>[today, tomorrow)</c> is exactly one day.</param>
public sealed record GetInspectionScheduleQuery(
    DateTime From,
    DateTime To,
    int? RequestingInspectorId,
    bool IncludeCompleted = true);

public sealed class GetInspectionScheduleQueryValidator : AbstractValidator<GetInspectionScheduleQuery>
{
    /// <summary>
    /// The window is capped so this unpaged read cannot be turned into "every Inspection ever" by
    /// asking for a century — the same reasoning that puts a maximum on a page size
    /// (Architecture.md §5.1).
    /// </summary>
    public const int MaxWindowDays = 366;

    public GetInspectionScheduleQueryValidator()
    {
        RuleFor(q => q.From).NotEqual(default(DateTime));
        RuleFor(q => q.To).GreaterThan(q => q.From);

        RuleFor(q => q)
            .Must(q => (q.To - q.From).TotalDays <= MaxWindowDays)
            .WithMessage($"The schedule window must not exceed {MaxWindowDays} days.");
    }
}

public sealed class GetInspectionScheduleQueryHandler(
    IValidator<GetInspectionScheduleQuery> validator,
    IInspectionQueries inspectionQueries)
    : IQueryHandler<GetInspectionScheduleQuery, IReadOnlyList<InspectionDetailDto>>
{
    public async Task<IReadOnlyList<InspectionDetailDto>> HandleAsync(
        GetInspectionScheduleQuery query,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        return await inspectionQueries.GetScheduledAsync(
            query.From,
            query.To,
            query.RequestingInspectorId,
            query.IncludeCompleted,
            cancellationToken);
    }
}
