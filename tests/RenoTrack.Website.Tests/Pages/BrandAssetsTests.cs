using System.Net;

namespace RenoTrack.Website.Tests.Pages;

/// <summary>
/// The deployment-asset mount at <c>/brand</c>, exercised through the real request pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>These properties were verified by hand against a published build before they were tests, and
/// that is exactly why they are tests now.</b> A manual result proves the code was right once; only
/// a test keeps it right. The mount is conditional on its directory existing, so without
/// <see cref="BrandAssetsFactory"/> the middleware never executes in this suite and none of these
/// claims is checked at all.
/// </para>
/// <para>
/// The traversal cases matter more than they look. <c>curl</c> and most clients normalise <c>..</c>
/// before sending, so a hand-run check can appear to prove containment while never having sent the
/// hostile path — <c>HttpClient</c> here is given already-escaped forms so the server sees them.
/// </para>
/// </remarks>
public sealed class BrandAssetsTests(BrandAssetsFactory factory) : IClassFixture<BrandAssetsFactory>
{
    // ---- What the mount serves ---------------------------------------------

    [Fact]
    public async Task The_logo_is_served_with_its_real_content_type()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/brand/logo.svg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// <b>The mount is not image-only, and that is a decision rather than an oversight.</b> No
    /// requirement restricts deployment assets to images, so it serves any extension the default
    /// content-type provider recognises. Pinned so the breadth of the surface is stated: everything
    /// an operator deliberately places in this directory with a known extension is public.
    /// </summary>
    [Fact]
    public async Task A_known_non_image_extension_is_also_served()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/brand/notice.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// <c>ServeUnknownFileTypes</c> is false, so an unrecognised extension is refused rather than
    /// guessed at — the browser never has to sniff a type this site did not state.
    /// </summary>
    [Fact]
    public async Task An_unknown_extension_is_not_served()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/brand/notes.weirdext");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_file_that_does_not_exist_is_not_served()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/brand/nope.svg");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- What the mount must never reach ------------------------------------

    /// <summary>
    /// Traversal, in the forms a client will not silently normalise away. Every one must stay inside
    /// the brand directory.
    /// </summary>
    [Theory]
    [InlineData("/brand/../appsettings.json")]
    [InlineData("/brand/../../etc/passwd")]
    [InlineData("/brand/..%2fappsettings.json")]
    [InlineData("/brand/%2e%2e/appsettings.json")]
    [InlineData("/brand/%2e%2e%2fappsettings.json")]
    [InlineData("/brand/....//appsettings.json")]
    [InlineData("/brand/..%5cappsettings.json")]
    [InlineData("/brand/%2e%2e%5c%2e%2e%5cetc%2fpasswd")]
    public async Task Traversal_cannot_escape_the_brand_directory(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The application's own files sit beside the brand directory at the content root. Naming one
    /// through the mount must not reach it.
    /// </summary>
    [Theory]
    [InlineData("/brand/RenoTrack.Website.dll")]
    [InlineData("/brand/appsettings.json")]
    [InlineData("/brand/web.config")]
    public async Task Application_files_are_not_reachable_through_the_mount(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// <b>A real file, with a known extension, that exists — and is one directory too high.</b> The
    /// other refusals could in principle pass because the file simply is not there; this one exists,
    /// is served happily from inside the directory, and must still be unreachable from outside it.
    /// That is what proves the <c>PhysicalFileProvider</c> root is the boundary.
    /// </summary>
    [Fact]
    public async Task A_real_file_outside_the_brand_directory_cannot_be_served_through_the_mount()
    {
        Assert.True(File.Exists(Path.Combine(factory.ContentRoot, BrandAssetsFactory.SiblingFileName)));

        using var client = factory.CreateClient();

        using var direct = await client.GetAsync($"/brand/{BrandAssetsFactory.SiblingFileName}");
        using var traversed = await client.GetAsync(
            new Uri($"/brand/..%2f{BrandAssetsFactory.SiblingFileName}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, direct.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, traversed.StatusCode);
    }

    /// <summary>A directory is not a file: no listing, no browsing.</summary>
    [Fact]
    public async Task The_brand_directory_itself_cannot_be_listed()
    {
        using var client = factory.CreateClient();

        using var withSlash = await client.GetAsync("/brand/");
        using var withoutSlash = await client.GetAsync("/brand");

        Assert.NotEqual(HttpStatusCode.OK, withSlash.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, withoutSlash.StatusCode);
    }

    // ---- Headers ------------------------------------------------------------

    /// <summary>
    /// A logo is public, non-secret content on a route with no <c>token</c> parameter, so it gets
    /// the baseline headers and — correctly — not the credential-page rules: it *should* be
    /// cacheable, and the quote page that embeds it is still <c>no-store</c>.
    /// </summary>
    [Fact]
    public async Task A_brand_asset_carries_the_baseline_headers_and_may_be_cached()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/brand/logo.svg");

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));

        Assert.False(response.Headers.Contains("X-Robots-Tag"));
        Assert.NotEqual(true, response.Headers.CacheControl?.NoStore);
        Assert.NotNull(response.Headers.ETag);
    }
}
