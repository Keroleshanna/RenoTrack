using FluentValidation;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Customers.Queries.GetCustomerById;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Customers.Queries.GetCustomerById;

/// <summary>The Admin's address panel read (Phase 14 Slice 2b, D112).</summary>
public class GetCustomerByIdQueryHandlerTests
{
    private readonly FakeCustomerRepository _customerRepository = new();
    private readonly GetCustomerByIdQueryHandler _handler;

    public GetCustomerByIdQueryHandlerTests()
    {
        _handler = new GetCustomerByIdQueryHandler(new GetCustomerByIdQueryValidator(), _customerRepository);
    }

    [Fact]
    public async Task ReturnsTheCustomersIdentityAndAddress()
    {
        var customer = _customerRepository.Seed(Customer.Create(
            leadId: 11, "Erika Mustermann", "erika@example.test", "+49 000 1234567", "Musterstr. 1"));

        var dto = await _handler.HandleAsync(new GetCustomerByIdQuery(customer.Id), CancellationToken.None);

        Assert.Equal(customer.Id, dto.Id);
        Assert.Equal(11, dto.LeadId);
        Assert.Equal("Erika Mustermann", dto.Name);
        Assert.Equal("Musterstr. 1", dto.Address);
    }

    [Fact]
    public async Task ReturnsANullAddressForACustomerThatHasNone()
    {
        var customer = _customerRepository.Seed(Customer.Create(
            leadId: 11, "Erika Mustermann", "erika@example.test", "+49 000 1234567"));

        var dto = await _handler.HandleAsync(new GetCustomerByIdQuery(customer.Id), CancellationToken.None);

        Assert.Null(dto.Address);
    }

    [Fact]
    public async Task AnUnknownCustomerIsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new GetCustomerByIdQuery(999), CancellationToken.None));
    }

    [Fact]
    public async Task ANonPositiveIdIsAValidationFailure()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new GetCustomerByIdQuery(0), CancellationToken.None));
    }
}
