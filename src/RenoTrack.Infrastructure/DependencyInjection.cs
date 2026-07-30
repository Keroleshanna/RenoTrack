using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Application.CatalogItems;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Infrastructure.Email;
using RenoTrack.Infrastructure.FileStorage;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Queries;
using RenoTrack.Infrastructure.Persistence.Repositories;

namespace RenoTrack.Infrastructure;

/// <summary>
/// Infrastructure-only composition (Slices 3-15) — registers RenoTrackDbContext, every
/// repository/query/service built in Slices 3-13, and Identity (Slice 15). Deliberately does NOT
/// register IOwnershipValidator: its concrete implementation lives in RenoTrack.Application, not
/// Infrastructure (CLAUDE.md §9) — that registration, along with FluentValidation validators and
/// command handlers, belongs to a future AddApplication() extension, not here.
///
/// Every registration is Scoped, matching RenoTrackDbContext's own Scoped lifetime
/// (AddDbContext's default) — this is what makes "repository adds an entity ->
/// UnitOfWork.SaveChangesAsync() commits it" work at all (D48). PlaceholderFileStorage and
/// LoggingNoOpEmailSender have no dependencies today but are still registered Scoped, not
/// Singleton: their real Phase 4/Phase 9 implementations may need a Scoped dependency (a
/// DbContext, IHttpContextAccessor, etc.), and a Singleton wrapping a future Scoped dependency is
/// a classic captive-dependency bug. One uniform lifetime rule across every Infrastructure
/// registration removes that whole class of mistake before it can happen.
///
/// AddIdentityCore (not AddIdentity) — D54: AddIdentity also wires cookie-authentication-scheme
/// defaults meant for server-rendered web apps; this API commits to JWT bearer tokens
/// (Architecture.md §7.1), not cookies. No AddAuthentication()/AddJwtBearer()/UseAuthentication()
/// wiring here — that's Phase 4's concern (Slice 15 prepares storage only).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("RenoTrackDb")
            ?? throw new InvalidOperationException("Connection string 'RenoTrackDb' is not configured.");

        services.AddDbContext<RenoTrackDbContext>(options => options.UseSqlServer(connectionString));

        // No AddDefaultTokenProviders() — nothing yet needs password-reset/email-confirmation
        // tokens (growth-on-demand, CLAUDE.md §4); add it only when a real command needs it.
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<int>>()
            .AddEntityFrameworkStores<RenoTrackDbContext>();

        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<IInspectionRepository, InspectionRepository>();
        services.AddScoped<IAngebotRepository, AngebotRepository>();
        services.AddScoped<IAngebotReviewCommentRepository, AngebotReviewCommentRepository>();
        services.AddScoped<ICatalogItemRepository, CatalogItemRepository>();
        services.AddScoped<ICatalogItemQueries, CatalogItemQueries>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<INumberGeneratorService, NumberGeneratorService>();
        services.AddScoped<IFileStorage, PlaceholderFileStorage>();
        services.AddScoped<IEmailSender, LoggingNoOpEmailSender>();

        return services;
    }
}
