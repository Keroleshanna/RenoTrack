using System.Diagnostics;
using System.Text.Json.Serialization;
using RenoTrack.Api.ErrorHandling;
using RenoTrack.Api.OpenApi;
using RenoTrack.Application;
using RenoTrack.Infrastructure;
using RenoTrack.Infrastructure.Identity;
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
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

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

// Storage-only Identity setup (Slice 15) — seeds the two roles, nothing more. No
// authentication/JWT wiring here; that's Phase 4's concern.
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<IdentityRoleSeeder>();
    await seeder.SeedRolesAsync();
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

// Authentication must precede authorization: the former establishes who the caller is, the latter
// decides what they may do with that identity.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
