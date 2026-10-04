using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Api.Customers.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Customers.Commands.CorrectCustomerAddress;
using RenoTrack.Application.Customers.Dtos;
using RenoTrack.Application.Customers.Queries.GetCustomerById;

namespace RenoTrack.Api.Controllers;

/// <summary>
/// The customer's address, read and corrected by an Admin (BR-5, SRS FR-7.5,
/// <c>PermissionMatrix.md</c> §5, Phase 14 Slice 2b, D112).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two actions and no more.</b> D91's refusal of a Customers workspace stands: there is no list,
/// no search, and no correction of name, email or phone. The read exists because the Admin must see
/// the address they are about to correct; it is here, behind this controller's Admin-only attribute,
/// rather than on the Inspector-readable Project detail read, which is unscoped.
/// </para>
/// <para>
/// <b>Admin only, at the class level.</b> Both rows are Admin <c>F</c> / Inspector <c>—</c>. Being a
/// single-role controller there is no scope to derive and so no fall-through that could fail open,
/// and no ownership check belongs in either handler (CLAUDE.md §16). An Inspector is refused by the
/// authorization middleware with an empty 403, which is what the API tests assert.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = Roles.Admin)]
public sealed class CustomersController(
    IQueryHandler<GetCustomerByIdQuery, CustomerDto> getCustomerByIdHandler,
    ICommandHandler<CorrectCustomerAddressCommand, CustomerDto> correctAddressHandler) : ControllerBase
{
    /// <summary>One Customer's identity and address, for the Admin's address panel.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var customer = await getCustomerByIdHandler.HandleAsync(
            new GetCustomerByIdQuery(id),
            cancellationToken);

        return Ok(customer);
    }

    /// <summary>
    /// Sets or corrects the Customer's address, which BR-5 prints on every invoice.
    /// </summary>
    /// <remarks>
    /// <c>PUT</c> on the address sub-resource, matching <c>PUT /api/v1/leads/{id}/inspector</c>: the
    /// body replaces the one value. The address is required, so this sets or corrects and never
    /// clears — a blank value is a field-keyed 400.
    /// </remarks>
    [HttpPut("{id:int}/address")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CorrectAddress(
        int id,
        CorrectCustomerAddressRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await correctAddressHandler.HandleAsync(
            new CorrectCustomerAddressCommand(
                CustomerId: id,
                request.Address,
                CorrectedByAdminId: CurrentUserId()),
            cancellationToken);

        return Ok(customer);
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
