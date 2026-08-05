using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Api.CatalogItems.Dtos;
using RenoTrack.Application.CatalogItems.Commands.CreateCatalogItem;
using RenoTrack.Application.CatalogItems.Commands.RetireCatalogItem;
using RenoTrack.Application.CatalogItems.Commands.SaveAngebotItemAsCatalogItem;
using RenoTrack.Application.CatalogItems.Commands.UpdateCatalogItem;
using RenoTrack.Application.CatalogItems.Dtos;
using RenoTrack.Application.CatalogItems.Queries.SearchCatalogItems;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;

namespace RenoTrack.Api.Controllers;

/// <summary>
/// Catalog endpoints (Architecture.md §5.2, PermissionMatrix.md §6, Wireframes D2 and F1).
/// </summary>
/// <remarks>
/// <para>
/// <b>The Catalog is shared company-wide, so nothing here is ownership-scoped.</b> Every action is
/// either "F" for both roles (viewing) or "F" for one role (Admin curation, Inspector save-as) — no
/// row belongs to a caller, so <c>IOwnershipValidator</c> appears nowhere in this controller's call
/// graph. That is the correct reflection of PermissionMatrix.md §6, not an omission (CLAUDE.md §16).
/// </para>
/// <para>
/// The save-as route nests under <c>angebot-items</c> but lives here, following
/// <c>InspectionsController</c>'s precedent: it creates a <c>CatalogItem</c>, and Architecture.md
/// §5.2 lists it under Catalog. Cohesion by resource beats cohesion by URL prefix.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/catalog-items")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Inspector}")]
public sealed class CatalogItemsController(
    IQueryHandler<SearchCatalogItemsQuery, PagedResult<CatalogItemDto>> searchHandler,
    ICommandHandler<CreateCatalogItemCommand, CatalogItemDto> createHandler,
    ICommandHandler<UpdateCatalogItemCommand, CatalogItemDto> updateHandler,
    ICommandHandler<RetireCatalogItemCommand, CatalogItemDto> retireHandler,
    ICommandHandler<SaveAngebotItemAsCatalogItemCommand, CatalogItemDto> saveAsCatalogItemHandler) : ControllerBase
{
    /// <summary>
    /// The Catalog picker and list (Wireframes D2, F1). Both roles, unscoped.
    /// </summary>
    /// <remarks>
    /// Retired items never appear and there is no flag to include them (BR-12, D37) — retirement
    /// affects discovery only, and a retired item remains a valid direct reference for a new
    /// AngebotItem (BR-14).
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<PagedResult<CatalogItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery] string? searchTerm,
        [FromQuery] int page = Pagination.FirstPage,
        [FromQuery] int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await searchHandler.HandleAsync(
            new SearchCatalogItemsQuery(searchTerm, page, pageSize), cancellationToken);

        return Ok(result);
    }

    /// <summary>Creates a Catalog entry directly (PermissionMatrix.md §6). Admin only.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<CatalogItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        CreateCatalogItemRequest request,
        CancellationToken cancellationToken)
    {
        var catalogItem = await createHandler.HandleAsync(
            new CreateCatalogItemCommand(
                request.Title,
                request.DefaultUnitCode,
                request.SuggestedUnitPrice,
                request.DefaultSpecification,
                CreatedByAdminUserId: CurrentUserId()),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, catalogItem);
    }

    /// <summary>
    /// Edits a Catalog entry (PermissionMatrix.md §6). Admin only, so one Inspector's edit cannot
    /// surprise others; BR-8 already protects past Angebote from any change here.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<CatalogItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id,
        UpdateCatalogItemRequest request,
        CancellationToken cancellationToken)
    {
        var catalogItem = await updateHandler.HandleAsync(
            new UpdateCatalogItemCommand(
                id,
                request.Title,
                request.DefaultUnitCode,
                request.SuggestedUnitPrice,
                request.DefaultSpecification,
                UpdatedByAdminUserId: CurrentUserId()),
            cancellationToken);

        return Ok(catalogItem);
    }

    /// <summary>
    /// Retires a Catalog entry. Admin only.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not <c>DELETE</c>.</b> BR-12 makes "delete" mean retirement — the row is kept
    /// so any AngebotItem created from it keeps a valid <c>CatalogItemId</c> trace link (BR-8), and
    /// BR-14 keeps it usable as a direct reference. A <c>DELETE</c> verb here would advertise a
    /// physical removal that must never happen. This is the same reasoning that makes Leads and
    /// Invoices non-deletable.
    /// </remarks>
    [HttpPost("{id:int}/retire")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<CatalogItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Retire(int id, CancellationToken cancellationToken)
    {
        var catalogItem = await retireHandler.HandleAsync(
            new RetireCatalogItemCommand(id, RetiredByAdminUserId: CurrentUserId()),
            cancellationToken);

        return Ok(catalogItem);
    }

    /// <summary>
    /// Promotes an Angebot line item into a Catalog entry (SRS FR-4.10). Inspector only.
    /// </summary>
    /// <remarks>
    /// <b>Any</b> Inspector, not just the one who owns the Angebot: PermissionMatrix.md §3 marks this
    /// "F" because the Catalog is shared company-wide. Admin is excluded here — their curation path
    /// is <c>POST /api/v1/catalog-items</c>, which records no provenance.
    /// </remarks>
    [HttpPost("/api/v1/angebot-items/{angebotItemId:int}/save-as-catalog-item")]
    [Authorize(Roles = Roles.Inspector)]
    [ProducesResponseType<CatalogItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveAsCatalogItem(int angebotItemId, CancellationToken cancellationToken)
    {
        var catalogItem = await saveAsCatalogItemHandler.HandleAsync(
            new SaveAngebotItemAsCatalogItemCommand(angebotItemId, SavedByInspectorId: CurrentUserId()),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, catalogItem);
    }

    /// <summary>The authenticated caller's user id, from the token's subject claim (D61).</summary>
    private int CurrentUserId()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(subject, out var userId)
            ? userId
            : throw new ForbiddenException("Authenticated principal has no usable subject claim.");
    }
}
