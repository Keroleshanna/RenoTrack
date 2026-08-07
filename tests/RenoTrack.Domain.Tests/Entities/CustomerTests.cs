using System.Reflection;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Domain.Tests.Entities;

public class CustomerTests
{
    private const int ValidLeadId = 42;
    private const string ValidName = "M. Klein";
    private const string ValidEmail = "m.klein@example.com";
    private const string ValidPhone = "0176 1234567";

    private static Customer CreateValid() =>
        Customer.Create(ValidLeadId, ValidName, ValidEmail, ValidPhone);

    // ---- Create -------------------------------------------------------

    [Fact]
    public void Create_PreservesProvidedValues()
    {
        var customer = Customer.Create(
            ValidLeadId, ValidName, ValidEmail, ValidPhone,
            address: "Musterstr. 1, 12345 Berlin");

        Assert.Equal(ValidLeadId, customer.LeadId);
        Assert.Equal(ValidName, customer.Name);
        Assert.Equal(ValidEmail, customer.Email);
        Assert.Equal(ValidPhone, customer.Phone);
        Assert.Equal("Musterstr. 1, 12345 Berlin", customer.Address);
    }

    /// <summary>
    /// The whole reason ERD.md's non-null <c>Customers.Address</c> was corrected: the public
    /// website contact form does not collect an address, so <c>Lead.Address</c> is legitimately
    /// null at conversion time. Requiring one here would block an otherwise valid
    /// <c>CustomerApproved</c> Angebot from ever becoming a Project.
    /// </summary>
    [Fact]
    public void Create_AllowsOmittingAddress()
    {
        var customer = CreateValid();

        Assert.Null(customer.Address);
    }

    [Fact]
    public void Create_TrimsProvidedValues()
    {
        var customer = Customer.Create(
            ValidLeadId, "  M. Klein  ", "  m.klein@example.com  ", "  0176 1234567  ",
            address: "  Musterstr. 1  ");

        Assert.Equal("M. Klein", customer.Name);
        Assert.Equal("m.klein@example.com", customer.Email);
        Assert.Equal("0176 1234567", customer.Phone);
        Assert.Equal("Musterstr. 1", customer.Address);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsNonPositiveLeadId(int leadId)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Customer.Create(leadId, ValidName, ValidEmail, ValidPhone));

        Assert.Equal("leadId", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_RejectsMissingName(string? name)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Customer.Create(ValidLeadId, name!, ValidEmail, ValidPhone));

        Assert.Equal("name", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_RejectsMissingEmail(string? email)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Customer.Create(ValidLeadId, ValidName, email!, ValidPhone));

        Assert.Equal("email", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_RejectsMissingPhone(string? phone)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Customer.Create(ValidLeadId, ValidName, ValidEmail, phone!));

        Assert.Equal("phone", ex.ParamName);
    }

    [Fact]
    public void Create_LeavesIdUnassigned()
    {
        var customer = CreateValid();

        Assert.Equal(0, customer.Id);
    }

    // ---- Structure ----------------------------------------------------

    /// <summary>
    /// CLAUDE.md §2: construction happens only through the named factory, so an invalid initial
    /// state is structurally unreachable rather than merely discouraged.
    /// </summary>
    [Fact]
    public void HasNoPublicConstructor()
    {
        var publicConstructors = typeof(Customer)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(publicConstructors);
    }

    /// <summary>
    /// CLAUDE.md §2: independent aggregates relate by id only. A navigation property to
    /// <see cref="Lead"/> would let a caller mutate a Lead through a Customer and would drag
    /// Lead's object graph into every Customer load. Generic type arguments are inspected too,
    /// so a hidden <c>List&lt;Lead&gt;</c> could not slip past.
    /// </summary>
    [Fact]
    public void HasNoReferenceToLeadAsAType()
    {
        var referencedTypes = typeof(Customer)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .Concat(typeof(Customer)
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(f => f.FieldType))
            .SelectMany(t => new[] { t }.Concat(t.GenericTypeArguments));

        Assert.DoesNotContain(typeof(Lead), referencedTypes);
    }

    /// <summary>
    /// The copy-at-creation guarantee (the same reasoning BR-8 applies to <c>AngebotItem</c>):
    /// once the work is committed, nothing may rewrite who it was agreed with. There is no
    /// mutator at all, so this holds structurally.
    /// </summary>
    [Fact]
    public void ExposesNoPublicMutator()
    {
        var publicMutators = typeof(Customer)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToArray();

        Assert.Empty(publicMutators);
    }

    [Fact]
    public void ExposesNoPublicSetters()
    {
        var settableProperties = typeof(Customer)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .ToArray();

        Assert.Empty(settableProperties);
    }
}
