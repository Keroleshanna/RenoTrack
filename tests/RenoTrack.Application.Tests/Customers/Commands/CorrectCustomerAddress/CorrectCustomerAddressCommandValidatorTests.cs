using RenoTrack.Application.Customers.Commands.CorrectCustomerAddress;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Customers.Commands.CorrectCustomerAddress;

/// <summary>
/// Shape only (CLAUDE.md §5). The limit is <see cref="Customer.MaxAddressLength"/> — the Customer's
/// own constant (D112) — so the validator, the Domain guard and the column cannot disagree.
/// </summary>
public class CorrectCustomerAddressCommandValidatorTests
{
    private readonly CorrectCustomerAddressCommandValidator _validator = new();

    private static CorrectCustomerAddressCommand Valid(string address = "Musterstr. 1") =>
        new(CustomerId: 5, address, CorrectedByAdminId: 2);

    [Fact]
    public void AcceptsAPresentAddress()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void AcceptsExactlyTheMaximumLength()
    {
        Assert.True(_validator.Validate(Valid(new string('a', Customer.MaxAddressLength))).IsValid);
    }

    [Fact]
    public void RejectsOneCharacterOverTheMaximumLength()
    {
        var result = _validator.Validate(Valid(new string('a', Customer.MaxAddressLength + 1)));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CorrectCustomerAddressCommand.Address));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RejectsAMissingAddress(string? address)
    {
        var result = _validator.Validate(Valid(address!));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CorrectCustomerAddressCommand.Address));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsANonPositiveCustomerId(int customerId)
    {
        var result = _validator.Validate(Valid() with { CustomerId = customerId });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CorrectCustomerAddressCommand.CustomerId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsANonPositiveActingAdminId(int adminId)
    {
        var result = _validator.Validate(Valid() with { CorrectedByAdminId = adminId });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CorrectCustomerAddressCommand.CorrectedByAdminId));
    }
}
