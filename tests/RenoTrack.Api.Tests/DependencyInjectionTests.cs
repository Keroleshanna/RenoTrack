using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Application;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Infrastructure;

namespace RenoTrack.Api.Tests;

/// <summary>
/// The safety net that makes explicit registration in <c>RenoTrack.Application.DependencyInjection</c> safe:
/// these tests reflect over RenoTrack.Application to discover every handler and validator that
/// exists, then assert each one actually resolves. Adding a handler without registering it fails
/// here immediately.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately the inverse of the production code's stance: reflection belongs in tests,
/// never in the composition root (CLAUDE.md §14 sanctions reflection-based tests specifically;
/// assembly scanning in production was rejected for the same reasons MediatR was, D22).
/// </para>
/// <para>
/// Why this lives in RenoTrack.Api.Tests rather than RenoTrack.Application.Tests: the handlers can
/// only be resolved with <c>AddApplication()</c> <em>and</em> <c>AddInfrastructure()</c> both
/// present — <c>AddApplication()</c> alone cannot satisfy <c>ILeadRepository</c>. The composition
/// root is where the two meet, so that is where the composition is tested. Resolving against the
/// Application project's own test fakes instead would prove the wrong thing.
/// </para>
/// <para>
/// No database connection is opened: <c>AddDbContext</c> constructs a <c>DbContext</c> without
/// connecting, which the equivalent Infrastructure test already relies on. The connection string
/// below only has to be well-formed, not reachable.
/// </para>
/// </remarks>
public sealed class DependencyInjectionTests
{
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RenoTrackDb"] =
                    "Server=(localdb)\\MSSQLLocalDB;Database=RenoTrackDiTests;Trusted_Connection=True;TrustServerCertificate=True",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        // The same validation the real host performs in Development — proves no registration
        // captures a shorter-lived dependency and that every dependency can actually be built.
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    /// <summary>
    /// Every closed ICommandHandler&lt;,&gt;/IQueryHandler&lt;,&gt; interface implemented by a
    /// concrete type in RenoTrack.Application. Discovered rather than listed, so a newly-added
    /// handler is covered without anyone remembering to update this test.
    /// </summary>
    public static TheoryData<Type> HandlerInterfaces()
    {
        var data = new TheoryData<Type>();

        foreach (var serviceType in ConcreteApplicationTypes()
            .SelectMany(type => type.GetInterfaces())
            .Where(i => i.IsGenericType
                && (i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                    || i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
            .Distinct())
        {
            data.Add(serviceType);
        }

        return data;
    }

    /// <summary>
    /// Every closed IValidator&lt;T&gt; implemented by a concrete AbstractValidator in
    /// RenoTrack.Application, discovered the same way.
    /// </summary>
    public static TheoryData<Type> ValidatorInterfaces()
    {
        var data = new TheoryData<Type>();

        foreach (var serviceType in ConcreteApplicationTypes()
            .SelectMany(type => type.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))
            .Distinct())
        {
            data.Add(serviceType);
        }

        return data;
    }

    private static IEnumerable<Type> ConcreteApplicationTypes() =>
        typeof(ICommandHandler<,>).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false });

    [Fact]
    public void Container_builds_with_validation_enabled()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider);
    }

    [Theory]
    [MemberData(nameof(HandlerInterfaces))]
    public void Every_handler_in_the_Application_assembly_resolves(Type handlerInterface)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(handlerInterface));
    }

    [Theory]
    [MemberData(nameof(ValidatorInterfaces))]
    public void Every_validator_in_the_Application_assembly_resolves(Type validatorInterface)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(validatorInterface));
    }

    [Fact]
    public void OwnershipValidator_resolves_from_Application_not_Infrastructure()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var ownershipValidator = scope.ServiceProvider.GetRequiredService<IOwnershipValidator>();

        // Its implementation deliberately lives in RenoTrack.Application (CLAUDE.md §9) — it has no
        // external dependency that would justify an Infrastructure-side one.
        Assert.IsType<OwnershipValidator>(ownershipValidator);
        Assert.Equal(typeof(ICommandHandler<,>).Assembly, ownershipValidator.GetType().Assembly);
    }
}
