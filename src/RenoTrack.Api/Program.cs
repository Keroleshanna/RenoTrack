using System.Diagnostics;
using System.Text.Json.Serialization;
using RenoTrack.Api.ErrorHandling;
using RenoTrack.Api.OpenApi;
using RenoTrack.Api.RateLimiting;
using RenoTrack.Application;
using RenoTrack.Infrastructure;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);

// RFC 7807 error responses (Architecture.md §5.3). CustomizeProblemDetails is applied here rather
// than inside ProblemDetailsExceptionHandler so traceId is present on *every* ProblemDetails
// response, including ones ASP.NET produces itself with no exception involved (e.g. model-binding
// 400s), which a handler-only approach would miss.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        // Not Request.Path directly: on a route whose path segment is a customer token, the raw
        // path is a credential, and error responses are retained by proxies, telemetry and support
        // tooling far more widely than the requests that produced them (RouteDiagnostics).
        // Every other route is unaffected and still reports its real path.
        context.ProblemDetails.Instance ??= RouteDiagnostics.SafeInstance(context.HttpContext);
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

// Abuse protection for the anonymous token-link surface (Architecture.md §12, D65). Scoped to the
// public controller by an opt-in named policy, so no internal route can inherit it by accident.
builder.Services.AddPublicRateLimiting(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums serialize as names ("Website"), not ordinals (0). Three reasons, in order of
        // weight: an ordinal contract silently changes meaning if anyone reorders an enum, which is
        // an invisible breaking change for every existing client; the database already stores these
        // same enums as strings for exactly the readability reason ERD.md gives; and every project
        // document refers to statuses by name, so a numeric wire format would be the only place in
        // the system where they are not.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});

var app = builder.Build();

// Database readiness (D63). In Production this only *verifies* — migration history matches this
// build, required roles exist — and never writes, so the runtime login needs no DDL permission;
// schema changes are applied by an explicit deployment step beforehand. Development may set
// Database:Mode to Migrate to have startup apply migrations and seed roles instead. Failing here
// deliberately prevents the application from serving requests: an unreachable database, an
// incompatible migration history, or a missing role each mean traffic would misbehave rather than
// fail cleanly. No user account is ever created here — that is a separate step below, and it is
// unreachable in Production (SRS OQ-1 remains open).
using (var scope = app.Services.CreateScope())
{
    var databaseInitializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await databaseInitializer.InitializeAsync();
}

// Development login accounts (D64). A deliberately separate step, in its own scope, immediately
// after the database is known to be ready: "the database can serve requests" and "a convenience
// account exists" are different claims, and DatabaseInitializer makes only the first. This is a
// no-op unless DevelopmentBootstrap:Enabled is true, and it refuses outright in any environment
// other than Development, so no code path here can create an account in Production.
using (var scope = app.Services.CreateScope())
{
    var developmentBootstrap = scope.ServiceProvider.GetRequiredService<DevelopmentBootstrap>();
    await developmentBootstrap.RunAsync();
}

// Configure the HTTP request pipeline.

// First in the pipeline, so it catches exceptions thrown by everything after it.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Interactive API documentation over the document MapOpenApi already serves. Development-only,
    // matching MapOpenApi's own existing guard — the docs are a developer tool, not a public surface.
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

// Explicit, because the next middleware depends on an endpoint having been selected. Without this
// call routing would still run, but only immediately before MapControllers — after the capture
// below, which would then see nothing.
app.UseRouting();

// Records the matched route template while it still exists. ASP.NET's exception middleware clears
// the endpoint and route values before any IExceptionHandler runs, so a token-bearing path cannot
// be recognised as such later — and would be echoed verbatim into logs and ProblemDetails
// `instance`. See RouteDiagnostics for the full reasoning and how it was found.
app.Use(async (context, next) =>
{
    RouteDiagnostics.Capture(context);
    await next(context);
});

// After routing (the policy is selected from endpoint metadata) and after the capture above (so a
// 429 on a token route is redacted like every other response), and before authentication — the
// surface it protects is anonymous, so there is no identity to establish first, and throttling
// before authentication means an abusive caller cannot make the server do that work either.
app.UseRateLimiter();

// Authentication must precede authorization: the former establishes who the caller is, the latter
// decides what they may do with that identity.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
