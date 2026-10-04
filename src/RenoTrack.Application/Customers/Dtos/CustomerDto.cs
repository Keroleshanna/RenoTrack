using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Customers.Dtos;

/// <summary>
/// What the Admin's customer-address panel reads and what correcting the address returns
/// (Phase 14 Slice 2b, D112).
/// </summary>
/// <remarks>
/// <b>Deliberately narrow</b> (CLAUDE.md §7): exactly the fields the panel renders. <c>Email</c> and
/// <c>Phone</c> exist on <see cref="Customer"/> and are absent here on purpose — nothing reads them
/// through this contract, and a field added "because it exists" is how a personal-data surface grows
/// without anyone deciding it should. An API test pins the exact property set.
/// </remarks>
public sealed record CustomerDto(
    int Id,
    int LeadId,
    string Name,
    string? Address);

public static class CustomerMappingExtensions
{
    public static CustomerDto ToDto(this Customer customer) => new(
        customer.Id,
        customer.LeadId,
        customer.Name,
        customer.Address);
}
