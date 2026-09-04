using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RenoTrack.Application.CatalogItems;
using RenoTrack.Application.Inspections;
using RenoTrack.Application.Invoices;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Infrastructure.Email;
using RenoTrack.Infrastructure.FileStorage;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Queries;
using RenoTrack.Infrastructure.Persistence.Repositories;
using RenoTrack.Infrastructure.TokenLinks;

namespace RenoTrack.Infrastructure.Tests;

/// <summary>
/// Proves AddInfrastructure()'s registrations for real, not by inspection: the container is
/// built with ValidateOnBuild/ValidateScopes enabled (the same checks that catch a Singleton
/// accidentally capturing a Scoped DbContext), and every registered interface is resolved inside
/// a real scope. No database connection is actually opened — constructing a DbContext via
/// AddDbContext never connects, only executing a query would.
/// </summary>
public sealed class DependencyInjectionTests
{
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RenoTrackDb"] =
                    new SqlConnectionStringBuilder
                    {
                        DataSource = @"(localdb)\MSSQLLocalDB",
                        InitialCatalog = "RenoTrackDependencyInjectionTests",
                        IntegratedSecurity = true,
                        TrustServerCertificate = true,
                    }.ConnectionString,

                // AddInfrastructure validates this eagerly as of Slice 8, so the container cannot
                // even be built without it.
                ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "RenoTrackDiTests"),

                // Same category as FileStorage:RootPath above — validated eagerly as of Phase 6
                // Slice 1, so the container cannot be built without it.
                ["TokenLink:LifetimeDays"] = "30",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(); // Required for ILogger<T> resolution (AuditService, LoggingNoOpEmailSender) — the real host provides this for free via WebApplicationBuilder; an isolated container does not.
        // Same category as AddLogging above: DatabaseInitializer needs IHostEnvironment (D63's
        // Production guard reads it), which the generic host registers for free and an isolated
        // container does not.
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    [Fact]
    public void AddInfrastructure_MissingConnectionString_ThrowsAtRegistrationTime()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var exception = Record.Exception(() => { services.AddInfrastructure(configuration); });

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void BuildingTheContainer_WithValidateOnBuildAndValidateScopes_Succeeds()
    {
        // If any Singleton captured RenoTrackDbContext (Scoped), this call itself would throw.
        using var provider = BuildProvider();

        Assert.NotNull(provider);
    }

    [Fact]
    public void EveryRegisteredInfrastructureService_ResolvesToItsExpectedConcreteType()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        Assert.IsType<LeadRepository>(services.GetRequiredService<ILeadRepository>());
        Assert.IsType<InspectionRepository>(services.GetRequiredService<IInspectionRepository>());
        Assert.IsType<AngebotRepository>(services.GetRequiredService<IAngebotRepository>());
        Assert.IsType<AngebotReviewCommentRepository>(services.GetRequiredService<IAngebotReviewCommentRepository>());
        Assert.IsType<CatalogItemRepository>(services.GetRequiredService<ICatalogItemRepository>());
        Assert.IsType<CatalogItemQueries>(services.GetRequiredService<ICatalogItemQueries>());

        // Phase 10 reads. The last of these is Infrastructure-OWNED (D77): the Application-assembly
        // reflection test cannot see it, so this line is the only thing pinning its registration.
        Assert.IsType<InvoiceQueries>(services.GetRequiredService<IInvoiceQueries>());
        Assert.IsType<InspectionQueries>(services.GetRequiredService<IInspectionQueries>());
        Assert.IsType<UserDirectoryQueries>(services.GetRequiredService<IUserDirectoryQueries>());
        Assert.IsType<UnitOfWork>(services.GetRequiredService<IUnitOfWork>());
        Assert.IsType<AuditService>(services.GetRequiredService<IAuditService>());
        Assert.IsType<NumberGeneratorService>(services.GetRequiredService<INumberGeneratorService>());
        Assert.IsType<LocalDiskFileStorage>(services.GetRequiredService<IFileStorage>());
        Assert.IsType<LoggingNoOpEmailSender>(services.GetRequiredService<IEmailSender>());
        Assert.IsType<TokenLinkRepository>(services.GetRequiredService<ITokenLinkRepository>());
        Assert.IsType<TokenLinkService>(services.GetRequiredService<ITokenLinkService>());
    }

    [Fact]
    public void RepositoriesResolvedInTheSameScope_ShareTheSameDbContextInstance()
    {
        // The concrete proof behind D48's reasoning: repository.AddAsync + UnitOfWork.SaveChangesAsync
        // only works because they're the same DbContext instance within one scope.
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        var contextViaLeadRepository = services.GetRequiredService<RenoTrackDbContext>();
        var contextViaSecondResolution = services.GetRequiredService<RenoTrackDbContext>();

        Assert.Same(contextViaLeadRepository, contextViaSecondResolution);
    }
}
