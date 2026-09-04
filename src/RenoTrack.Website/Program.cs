using RenoTrack.Website.Content;
using RenoTrack.Website.PublicApi;
using RenoTrack.Website.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// The forwarding handler needs the current request's connection address (D97).
builder.Services.AddHttpContextAccessor();

// Eagerly validated and registered as a singleton, matching every options type in the API: a
// missing or plaintext API origin must fail startup naming the exact key, not surface later as a
// customer being told their quote is unavailable.
var publicApiOptions = builder.Configuration.GetSection(PublicApiOptions.SectionName).Get<PublicApiOptions>()
    ?? new PublicApiOptions();
publicApiOptions.Validate();
builder.Services.AddSingleton(publicApiOptions);

// Content rather than wiring, so bound through IOptions and never required — see
// CompanyIdentityOptions for why absence warns instead of failing.
builder.Services.Configure<CompanyIdentityOptions>(
    builder.Configuration.GetSection(CompanyIdentityOptions.SectionName));

// Built once here so a malformed proxy entry fails startup rather than silently shrinking the
// trust list. Empty by default: an unconfigured deployment trusts no forwarder at all.
var trustedForwarders = builder.Configuration.GetSection(TrustedForwardersOptions.SectionName)
    .Get<TrustedForwardersOptions>() ?? new TrustedForwardersOptions();
var forwardedHeadersOptions = trustedForwarders.Build();

builder.Services.AddTransient<ClientAddressForwardingHandler>();

// The one way this Website talks to the API. A typed client, so the boundary is a named interface
// a page can be tested against rather than an HttpClient call inside a page model.
builder.Services.AddHttpClient<IPublicAngebotClient, PublicAngebotClient>(client =>
    {
        client.BaseAddress = new Uri($"{publicApiOptions.NormalizedBaseUrl}/");
        client.Timeout = publicApiOptions.Timeout;
    })
    .AddHttpMessageHandler<ClientAddressForwardingHandler>()

    // Load-bearing, not tidiness. IHttpClientFactory attaches its own logging handlers, which write
    // "Sending HTTP request GET {uri}" at Information — and this client's every URI contains the
    // customer's token. Those handlers log under "System.Net.Http.HttpClient.*", not
    // "Microsoft.AspNetCore", so the Warning level appsettings.json sets for the latter does not
    // cover them: at the Default level of Information a live credential would be written to every
    // log sink on every page view. Removed structurally here rather than left to a log-level
    // setting, so no configuration change can reintroduce it (D97). appsettings.json pins the
    // category to Warning as well, as defence in depth.
    .RemoveAllLoggers();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Before everything that reads the client's address or the request scheme. Trusts only the
// forwarders configuration names; with none configured this is a no-op and Connection.RemoteIpAddress
// stays the immediate peer, which is the pre-D97 behaviour and never wrong, only less precise.
if (trustedForwarders.IsConfigured)
{
    app.UseForwardedHeaders(forwardedHeadersOptions);
}

app.UseHttpsRedirection();

app.UseRouting();

// After UseRouting, because the token-route rules read the matched endpoint's route values —
// registered earlier they would find none and the strict headers would silently never apply.
app.UseCustomerSecurityHeaders();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// Reported once, at startup, so an unset identity is visible to an operator rather than silently
// producing a nameless page. Deliberately a warning and not a failure: this is copy, not wiring.
var companyIdentity = builder.Configuration.GetSection(CompanyIdentityOptions.SectionName)
    .Get<CompanyIdentityOptions>();
if (companyIdentity?.HasDisplayName is not true)
{
    app.Logger.LogWarning(
        "Configuration '{Key}' is not set, so customer-facing pages render without a company name. " +
        "This is expected until the real company identity is supplied (Phase 11 Q7).",
        $"{CompanyIdentityOptions.SectionName}:{nameof(CompanyIdentityOptions.DisplayName)}");
}

app.Run();

// Top-level statements compile into an internal Program class; RenoTrack.Website.Tests names it as
// WebApplicationFactory<Program>'s entry point via InternalsVisibleTo, the same arrangement
// RenoTrack.Api already uses.
