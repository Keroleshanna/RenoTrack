using RenoTrack.Api.OpenApi;
using RenoTrack.Infrastructure;
using RenoTrack.Infrastructure.Identity;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();
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
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Interactive API documentation over the document MapOpenApi already serves. Development-only,
    // matching MapOpenApi's own existing guard — the docs are a developer tool, not a public surface.
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
