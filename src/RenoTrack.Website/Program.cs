using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.WebEncoders;
using RenoTrack.Website.Content;
using RenoTrack.Website.PublicApi;
using RenoTrack.Website.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// German text must reach the customer as German text.
//
// ASP.NET Core's default HtmlEncoder allows only Basic Latin through and escapes everything else to
// numeric character references, so "Wände" renders as "W&#xE4;nde", "m²" as "m&#xB2;" and every
// price as "1.234,56&#x20AC;". A browser displays those identically, which is exactly why this is
// easy to ship without noticing — but the served document is then neither readable in view-source
// nor searchable, and on a page whose whole audience is German-speaking that is the wrong default.
// It was found by CI: every failing assertion contained ä, ² or €, and every passing one was ASCII.
//
// **This does not weaken escaping.** The range setting governs which characters may pass through
// unescaped; the HTML-significant ones (< > & " ') are escaped regardless of range, so the encoding
// that makes Inspector-typed free text safe on this page is untouched. The document is served as
// UTF-8 and declares that charset, so the characters are unambiguous on the wire.
builder.Services.Configure<WebEncoderOptions>(options =>
{
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All);
});

// The forwarding handler needs the current request's connection address (D97).
builder.Services.AddHttpContextAccessor();

// Eagerly validated and registered as a singleton, matching every options type in the API: a
// missing or plaintext API origin must fail startup naming the exact key, not surface later as a
// customer being told their quote is unavailable.
var publicApiOptions = builder.Configuration.GetSection(PublicApiOptions.SectionName).Get<PublicApiOptions>()
    ?? new PublicApiOptions();
publicApiOptions.Validate();
builder.Services.AddSingleton(publicApiOptions);

// Content rather than wiring, so never required — see CompanyIdentityOptions for why absence warns
// instead of failing. Bound eagerly and registered as a validated singleton, the same shape as
// PublicApiOptions and LegalContentOptions: absence is fine, but a logo that would reach a third
// party or that no screen-reader can announce must fail startup rather than reach a customer.
var companyIdentity = builder.Configuration.GetSection(CompanyIdentityOptions.SectionName)
    .Get<CompanyIdentityOptions>() ?? new CompanyIdentityOptions();
companyIdentity.Validate();
builder.Services.AddSingleton(companyIdentity);

// The two legally required pages' content (SRS FR-1.4, D100). Registered as a validated singleton
// rather than through IOptions, matching PublicApiOptions: the pages and the layout need the same
// instance, and a malformed link must fail startup rather than reach a customer as a dead or
// dangerous anchor. Absent content is *not* malformed — it is the expected state until the company
// writes its text, and it makes the routes answer 404 rather than serve an empty page.
var legalContent = builder.Configuration.GetSection(LegalContentOptions.SectionName)
    .Get<LegalContentOptions>() ?? new LegalContentOptions();
legalContent.Validate();
builder.Services.AddSingleton(legalContent);

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

// Deployment-supplied brand assets (the company logo), served from a 'brand' directory beside the
// application rather than from wwwroot.
//
// MapStaticAssets below serves only the endpoints in its BUILD-TIME manifest, so a file an operator
// copies into wwwroot after publishing is on disk and still answers 404 — proven by serving one and
// getting exactly that. A deployment-supplied asset therefore needs a run-time file provider.
//
// Deliberately a dedicated directory rather than a blanket UseStaticFiles over wwwroot: this is a
// customer-facing origin, and the narrower mount serves only what a deployment deliberately placed
// there. ServeUnknownFileTypes stays false, so an unrecognised extension is not served at all
// rather than guessed at.
var brandRoot = Path.Combine(builder.Environment.ContentRootPath, CompanyIdentityOptions.BrandAssetsDirectoryName);
if (Directory.Exists(brandRoot))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(brandRoot),
        RequestPath = CompanyIdentityOptions.LogoPathPrefix.TrimEnd('/'),
        ServeUnknownFileTypes = false,
    });
}

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// Reported once, at startup, so an unset identity is visible to an operator rather than silently
// producing a nameless page. Deliberately a warning and not a failure: this is copy, not wiring.
if (!companyIdentity.HasDisplayName)
{
    app.Logger.LogWarning(
        "Configuration '{Key}' is not set, so customer-facing pages render without a company name. " +
        "This is expected until the real company identity is supplied (Phase 11 Q7).",
        $"{CompanyIdentityOptions.SectionName}:{nameof(CompanyIdentityOptions.DisplayName)}");
}

// The same shape, for the same reason: content that has not been written yet is visible to an
// operator rather than silently missing. Until each is supplied its route answers 404 and no link
// to it is rendered, so FR-1.4 stays open — the mechanism is complete, the requirement is not
// (D100 Part 1). Reported per document, because one may be written before the other.
// A configured logo that resolves to no file renders a broken image on every customer's quote.
// Checked against the directory that actually serves it, not against wwwroot: the first version of
// this check asked WebRootFileProvider, which reads the disk and therefore reported success for a
// file MapStaticAssets would never serve — an assertion that could not fail for the reason it was
// written for. A warning rather than a failure, because a container may mount the directory after
// the image is built.
if (companyIdentity.HasLogo)
{
    var logoFile = Path.Combine(
        brandRoot,
        companyIdentity.LogoPath!.Trim()[CompanyIdentityOptions.LogoPathPrefix.Length..]);

    if (!File.Exists(logoFile))
    {
        app.Logger.LogWarning(
            "Configuration '{Key}' is '{Path}', but no such file exists under '{BrandRoot}', so the " +
            "customer page will render a broken image.",
            $"{CompanyIdentityOptions.SectionName}:{nameof(CompanyIdentityOptions.LogoPath)}",
            companyIdentity.LogoPath,
            brandRoot);
    }
}

foreach (var (key, configured) in new[]
         {
             ($"{LegalContentOptions.SectionName}:{nameof(LegalContentOptions.Impressum)}", legalContent.Impressum.HasContent),
             ($"{LegalContentOptions.SectionName}:{nameof(LegalContentOptions.Datenschutz)}", legalContent.Datenschutz.HasContent),
         })
{
    if (!configured)
    {
        app.Logger.LogWarning(
            "Configuration '{Key}' has no content, so that page answers 404 and is not linked. " +
            "SRS FR-1.4 requires it before launch; the text is supplied by the company (Phase 11 Q7).",
            key);
    }
}

app.Run();

// Top-level statements compile into an internal Program class; RenoTrack.Website.Tests names it as
// WebApplicationFactory<Program>'s entry point via InternalsVisibleTo, the same arrangement
// RenoTrack.Api already uses.
