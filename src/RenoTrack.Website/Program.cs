using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WebEncoders;
using RenoTrack.Website.Content;
using RenoTrack.Website.PublicApi;
using RenoTrack.Website.Security;
using RenoTrack.Website.Site;

var builder = WebApplication.CreateBuilder(args);

// The company's content pack (D102), loaded before anything binds configuration so identity, site
// content and legal text all see it. Read from the host's own configuration — a pack cannot name its
// own location. Each pack file enters through an isolated provider that can contribute nothing outside
// that file's allowed sections, so the pack can never touch logging, the API origin, the forwarder
// trust list or any other product setting. Do not replace it with AddJsonFile.
var contentPack = builder.Configuration.GetSection(ContentPackOptions.SectionName).Get<ContentPackOptions>()
    ?? new ContentPackOptions();
contentPack.Validate();
contentPack.AddTo(((IConfigurationBuilder)builder.Configuration).Sources);

// Lists merge entry by entry across configuration sources, silently; a list must come from one source.
ContentListSourceGuard.EnsureSingleSource(builder.Configuration);


// Load-bearing, not tidiness: stops ASP.NET creating the per-request logging scope whose RequestPath
// is the customer's token on /angebot/{token}. Without it every warning or error logged during a
// token request carries the credential to any sink that writes scopes — on Windows, the Application
// event log by default. Post-configuration, so no appsettings.json or environment value can undo it.
// See HostingRequestScopeSuppression.
builder.Services.AddSingleton<IPostConfigureOptions<LoggerFilterOptions>, HostingRequestScopeSuppression>();

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

// The marketing site's canonical origin and services (D102). Validated against the identity it
// presents: an enabled site with half an identity fails startup naming every missing key, while a
// deployment without Site:PublicBaseUrl keeps exactly the behaviour it had before.
var site = builder.Configuration.GetSection(SiteOptions.SectionName).Get<SiteOptions>() ?? new SiteOptions();
site.Validate(companyIdentity);
builder.Services.AddSingleton(site);

// The marketing site's shape is decided here, once (D103). When it is enabled, the startup convention gives
// the marketing pages their endpoint metadata, and every request-time marketing behaviour — CSP, canonical
// paths, the site layout — decides from that metadata alone. When it is disabled, the convention is never
// registered, no endpoint carries the metadata, and the legal pages behave exactly as before Phase 13.
builder.Services.AddRazorPages(options =>
{
    if (site.IsEnabled)
    {
        MarketingPageConvention.Apply(options.Conventions);
    }
});

// Every published photo derivative, verified byte by byte before the site may start (D106): it must exist, stay within
// its budget, match its format and exact size, and carry no EXIF, XMP, IPTC or other metadata. Only files this catalog
// lists are ever served. Loaded only for an enabled site; a token-only deployment has no photos and no /medien/.
var mediaCatalog = site.IsEnabled
    ? MediaCatalog.Load(site, contentPack.MediaRootFor(builder.Environment.ContentRootPath))
    : MediaCatalog.Empty;

// A startup snapshot, so marketing pages render from the values the pipeline was composed with rather than
// re-reading options per request.
if (site.IsEnabled)
{
    builder.Services.AddSingleton(new MarketingSite(site, mediaCatalog));
}

var themeStylesheet = new ThemeStylesheet(site.Theme);
builder.Services.AddSingleton(themeStylesheet);

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

// Restores ASP.NET's per-request Activity, and with it TraceId/SpanId on every log entry, which the
// scope suppression above would otherwise take away. It listens to the activity source only and
// changes no logging rule, so the token-bearing RequestPath scope stays gone. See
// RequestActivityTracing.
RequestActivityTracing.Enable(app);

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

// Before routing, so the re-executed request is routed afresh. 404 only, GET/HEAD only, never from a token
// route (D103) — and only when the marketing site exists: a token-only deployment keeps its bare 404s.
if (site.IsEnabled)
{
    app.UseSiteNotFoundPage();
}

app.UseRouting();

// After UseRouting, because the token-route rules read the matched endpoint's route values —
// registered earlier they would find none and the strict headers would silently never apply.
app.UseCustomerSecurityHeaders();

// Marketing pages only, decided from the matched endpoint's metadata (D103). Token routes can never carry
// that metadata (MarketingPageGuard below), so their headers stay exactly as UseCustomerSecurityHeaders sets them.
app.UseMarketingSecurityHeaders();

// The canonical host (derived www alias) and canonical marketing paths, with the origin captured now. Only
// when the marketing site is enabled; nothing in it reads configuration per request.
if (site.IsEnabled)
{
    app.UseCanonicalRedirects(site.CanonicalOrigin);
}

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
//
// With a content pack configured, the mount serves the pack's own brand/ instead (D102) — and only
// that directory: site.json and legal.json sit beside it and are never reachable.
var brandRoot = contentPack.BrandRootFor(builder.Environment.ContentRootPath);
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

ThemeStylesheet.Map(app, themeStylesheet);

// Photos: an allowlist of verified derivatives, never a directory mount (D106, S5-2). The requested name is a key in
// the catalog, so no request can reach a file the manifest does not list.
if (site.IsEnabled)
{
    MediaEndpoint.Map(app, mediaCatalog);
}

// Marketing metadata must never reach a route whose URL is a customer credential. Checked against the endpoints
// actually built, by route parameter, so it also covers token routes nobody has written yet (D103).
MarketingPageGuard.EnsureNoTokenRoutes(MarketingPageGuard.EndpointsOf(app));

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
// A configured logo that resolves to no file is not served, so every customer's quote shows the
// image's alternative text — the company name — where the logo should be. That looks exactly like a
// deployment with no logo configured, which is why this warning is the only signal an operator gets.
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
            "Configuration '{Key}' is '{Path}', but no such file exists under '{BrandRoot}', so the logo " +
            "is not served and customer pages show the company name (the image's alternative text) in " +
            "its place.",
            $"{CompanyIdentityOptions.SectionName}:{nameof(CompanyIdentityOptions.LogoPath)}",
            companyIdentity.LogoPath,
            brandRoot);
    }
}

// Counts and a path only — never content values (D101 governs what this application logs).
if (contentPack.IsConfigured)
{
    app.Logger.LogInformation(
        "Content pack loaded from '{PackRoot}': marketing site {SiteState}, {ServiceCount} service(s), {PhotoCount} " +
        "photo(s) in {FileCount} verified file(s).",
        contentPack.ResolvedRootPath,
        site.IsEnabled ? "enabled" : "disabled",
        site.Services.Count,
        mediaCatalog.ImageCount,
        mediaCatalog.FileCount);
}

if (!site.IsEnabled)
{
    app.Logger.LogWarning(
        "Configuration '{Key}' is not set, so the marketing site is disabled; the customer token pages and " +
        "the legal pages are unaffected.",
        $"{SiteOptions.SectionName}:{nameof(SiteOptions.PublicBaseUrl)}");
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
