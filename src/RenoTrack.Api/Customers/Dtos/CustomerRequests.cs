namespace RenoTrack.Api.Customers.Dtos;

/// <summary>
/// The corrected address for <c>PUT /api/v1/customers/{id}/address</c> (Phase 14 Slice 2b, D112).
/// </summary>
/// <remarks>
/// <para>
/// One field, a strict subset of <c>CorrectCustomerAddressCommand</c>: the Customer comes from the
/// route and the acting Admin from the JWT (D61). Name, email and phone are not correctable, so the
/// request cannot carry them — a body that sends them anyway is bound to this record and they are
/// dropped by model binding, and an API test proves nothing else changes.
/// </para>
/// <para>
/// No validation attributes: <c>CorrectCustomerAddressCommandValidator</c> owns the field rule, so
/// one failure has one error shape.
/// </para>
/// </remarks>
public sealed record CorrectCustomerAddressRequest(string Address);
