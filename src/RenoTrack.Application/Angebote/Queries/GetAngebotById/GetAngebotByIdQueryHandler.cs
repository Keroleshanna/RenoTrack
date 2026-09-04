using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.CatalogItems;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Queries.GetAngebotById;

/// <summary>
/// Reads through <see cref="IAngebotRepository"/> rather than a projection, deliberately.
/// </summary>
/// <remarks>
/// <para>
/// CLAUDE.md §22 splits reads by shape: a single-resource read enforces ownership through
/// <see cref="IOwnershipValidator"/>, which needs the Domain entity, while D36 prefers projections
/// for aggregates with <c>Include</c> chains. Those pull in opposite directions for
/// <see cref="Angebot"/> — and the tension resolves cleanly here, because this endpoint returns the
/// <em>entire</em> tree anyway. Hydrating the aggregate is not overhead to be avoided; it is exactly
/// the data being asked for, and it hands the validator the entity for free.
/// </para>
/// <para>
/// A projection would additionally have to re-derive <c>VatBreakdown</c>, <c>Subtotal</c> and
/// <c>LineTotal</c> in SQL — three calculations Architecture.md §6.1 places in the aggregate. Doing
/// that twice, in two languages, is precisely the drift the computed-property decision exists to
/// prevent.
/// </para>
/// </remarks>
public sealed class GetAngebotByIdQueryHandler(
    IAngebotRepository angebotRepository,
    ICatalogItemQueries catalogItemQueries,
    IOwnershipValidator ownershipValidator) : IQueryHandler<GetAngebotByIdQuery, AngebotDetailDto>
{
    public async Task<AngebotDetailDto> HandleAsync(GetAngebotByIdQuery query, CancellationToken cancellationToken)
    {
        var angebot = await angebotRepository.GetByIdAsync(query.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), query.AngebotId);

        // Null means Admin, who is "F" for viewing any Angebot — so no ownership rule applies at
        // all, and calling the validator would be a semantic error (CLAUDE.md §16).
        if (query.RequestingInspectorId is { } inspectorId)
        {
            ownershipValidator.EnsureAngebotOwnership(angebot, inspectorId);
        }

        // Which lines have already been contributed to the Catalog (FR-4.10). One batched query for
        // the whole document rather than one per line — and it cannot come from the aggregate,
        // because CatalogItem is independent and related by id only (CLAUDE.md §2).
        var itemIds = angebot.Sections.SelectMany(section => section.Items).Select(item => item.Id).ToList();

        var savedToCatalog = itemIds.Count == 0
            ? new HashSet<int>()
            : (HashSet<int>)await catalogItemQueries
                .GetAngebotItemIdsWithCatalogEntryAsync(itemIds, cancellationToken);

        return angebot.ToDetailDto(savedToCatalog);
    }
}
