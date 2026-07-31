using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using RenoTrack.Application.CatalogItems;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Leads;
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

        // A dedicated service, not a static utility (D55) — matching every other Infrastructure
        // service's shape. Scoped, consistent with the rest of this file, even though it creates
        // its own child scopes internally (IServiceScopeFactory is a Singleton-safe capability,
        // not itself Scoped state) — no reason to depart from the uniform lifetime rule above.
        services.AddScoped<IdentityRoleSeeder>();

        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<IInspectionRepository, InspectionRepository>();
        services.AddScoped<IAngebotRepository, AngebotRepository>();
        services.AddScoped<IAngebotReviewCommentRepository, AngebotReviewCommentRepository>();
        services.AddScoped<ICatalogItemRepository, CatalogItemRepository>();
        services.AddScoped<ICatalogItemQueries, CatalogItemQueries>();
        services.AddScoped<ILeadQueries, LeadQueries>();
        services.AddScoped<IUserQueries, UserQueries>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<INumberGeneratorService, NumberGeneratorService>();
        services.AddScoped<IFileStorage, PlaceholderFileStorage>();
        services.AddScoped<IEmailSender, LoggingNoOpEmailSender>();

        return services;
    }

    /// <summary>
    /// JWT bearer authentication (Architecture.md §7.1). Deliberately a separate extension from
    /// <see cref="AddInfrastructure"/> rather than folded into it: that method is storage
    /// composition, and this is an HTTP authentication scheme, so <c>Program.cs</c> naming both
    /// explicitly is clearer than one method quietly doing both. It lives in Infrastructure rather
    /// than in RenoTrack.Api because it needs <see cref="JwtOptions"/> and the same signing key
    /// <see cref="TokenService"/> issues with — keeping issuance and validation configured from one
    /// place is what makes them impossible to drift apart.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bound and validated eagerly, not via the options pattern's lazy resolution: a missing or
        // too-short signing key must fail at startup with an actionable message, not at first login
        // with a generic 500. Same fail-fast shape as the connection-string check above.
        var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        options.Validate();

        services.AddSingleton(options);

        // Registered here rather than in AddInfrastructure, even though it is a persistence-touching
        // service: it depends on JwtOptions, which only this method provides. Splitting them would
        // leave AddInfrastructure advertising an ITokenService that cannot actually be constructed —
        // a container-validation failure for anyone composing storage without authentication.
        services.AddScoped<ITokenService, TokenService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                    ValidateLifetime = true,

                    // No clock skew. The default is five minutes, which would keep a 15-minute
                    // access token usable for twenty — a third longer than intended, silently.
                    ClockSkew = TimeSpan.Zero,
                };
            });

        return services;
    }
}
