using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>Write-side repository for the Customer aggregate.</summary>
public interface ICustomerRepository
{
    Task AddAsync(Customer customer, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the Customer belonging to a Lead, or null if that Lead has never been converted.
    /// Named for the exact business question rather than as a generic lookup (CLAUDE.md §4): the
    /// answer is meaningful because <c>Customers.LeadId</c> is unique, so this is at most one row.
    ///
    /// <para>
    /// <b>This is the only way the conversion resolves a Customer, deliberately.</b> There is no
    /// find-by-email, find-by-phone or fuzzy-match variant, and none should be added without a
    /// documented customer-identity rule — matching two Leads to one Customer is a policy decision
    /// with real consequences (merging strangers who share a Gmail alias, splitting a genuine
    /// repeat customer), and no project document specifies one. `ERD.md` records the consequence
    /// as a known limitation: a repeat customer arriving as a new Lead gets a second Customer row.
    /// </para>
    /// </summary>
    Task<Customer?> FindByLeadIdAsync(int leadId, CancellationToken cancellationToken);
}
