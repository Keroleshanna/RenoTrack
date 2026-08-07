using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// In-memory fake. <c>AddAsync</c> deliberately does <b>not</b> assign an Id — a real repository
/// does not either, since EF Core assigns identity at <c>SaveChangesAsync</c>. Assignment happens
/// in <see cref="FakeUnitOfWork"/>-driven test setup instead, via <see cref="Seed"/>, so a handler
/// that used <c>customer.Id</c> before saving would fail here exactly as it does against a real
/// database.
/// </summary>
public sealed class FakeCustomerRepository : ICustomerRepository
{
    private readonly Dictionary<int, Customer> _byLeadId = [];
    private int _nextId = 1;

    public List<Customer> AddedCustomers { get; } = [];

    /// <summary>
    /// Set to have <see cref="AddAsync"/> assign an Id, simulating the save that follows it. The
    /// conversion handler saves immediately after adding, so its happy path needs this; a test
    /// proving the handler cannot use an unsaved Id leaves it false.
    /// </summary>
    public bool AssignIdOnAdd { get; set; } = true;

    public Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        AddedCustomers.Add(customer);

        if (AssignIdOnAdd)
        {
            AssignId(customer, _nextId++);
            _byLeadId[customer.LeadId] = customer;
        }

        return Task.CompletedTask;
    }

    public Task<Customer?> FindByLeadIdAsync(int leadId, CancellationToken cancellationToken) =>
        Task.FromResult(_byLeadId.GetValueOrDefault(leadId));

    /// <summary>
    /// Test-only seam simulating a Customer that already exists with a real assigned Id — EF
    /// Core's job in production. Reflection, because <c>Customer.Id</c> has no public setter by
    /// design; test code only, never production.
    /// </summary>
    public Customer Seed(Customer customer)
    {
        AssignId(customer, _nextId++);
        _byLeadId[customer.LeadId] = customer;
        return customer;
    }

    private static void AssignId(Customer customer, int id) =>
        typeof(Customer).GetProperty(nameof(Customer.Id))!.SetValue(customer, id);
}
