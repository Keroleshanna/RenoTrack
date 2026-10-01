using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static RenoTrack.Website.Tests.Site.MediaBytes;

namespace RenoTrack.Website.Tests.Site;

/// <summary>
/// <c>/medien/{datei}</c>: the allowlist endpoint (<b>D106</b>, S5-2, S5-12). It serves exactly the verified files the
/// manifest lists, with a fixed content type and immutable caching, and answers 404 to everything else.
/// </summary>
public sealed class MediaEndpointTests(MarketingSiteFixture site) : IClassFixture<MarketingSiteFixture>
{
    public static TheoryData<string, string> ListedFiles() => new()
    {
        { "testbild-startseite-480.webp", "image/webp" },
        { "testbild-startseite-960.webp", "image/webp" },
        { "testbild-startseite-1600.webp", "image/webp" },
        { "testbild-startseite-480.jpg", "image/jpeg" },
        { "testbild-startseite-960.jpg", "image/jpeg" },
        { "testbild-startseite-1600.jpg", "image/jpeg" },
        { "testbild-startseite-og.jpg", "image/jpeg" },
        { "testbild-leistung-eins-1600.webp", "image/webp" },
        { "testbild-leistung-eins-og.jpg", "image/jpeg" },
    };

    [Theory]
    [MemberData(nameof(ListedFiles))]
    public async Task A_listed_file_is_served_byte_for_byte_with_its_type_and_immutable_caching(string fileName, string contentType)
    {
        using var client = site.Client();

        using var response = await client.GetAsync($"/medien/{fileName}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal(Fixture(fileName), await response.Content.ReadAsByteArrayAsync());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task A_head_request_answers_200_without_a_body()
    {
        using var client = site.Client();

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/medien/testbild-startseite-960.jpg"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task The_version_query_does_not_change_what_is_served()
    {
        using var client = site.Client();

        using var response = await client.GetAsync("/medien/testbild-startseite-960.jpg?v=0000000000000000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Fixture("testbild-startseite-960.jpg"), await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// Nothing but an exact listed name: a real derivative on disk that no item lists, the pack's own files, case
    /// variants and every traversal form all answer 404 — and never the content of the file they aim at.
    /// </summary>
    [Theory]
    [InlineData("/medien/testbild-leistung-zwei-960.jpg")]
    [InlineData("/medien/testbild-leistung-zwei-1600.webp")]
    [InlineData("/medien/TESTBILD-STARTSEITE-960.JPG")]
    [InlineData("/medien/testbild-startseite-960.JPG")]
    [InlineData("/medien/site.json")]
    [InlineData("/medien/../site.json")]
    [InlineData("/medien/..%2fsite.json")]
    [InlineData("/medien/%2e%2e%2fsite.json")]
    [InlineData("/medien/..%5csite.json")]
    [InlineData("/medien/%2e%2e/legal.json")]
    [InlineData("/medien/..%2fbrand%2flogo.svg")]
    [InlineData("/medien/")]
    [InlineData("/medien")]
    [InlineData("/medien/testbild-startseite-960")]
    [InlineData("/media/testbild-startseite-960.jpg")]
    public async Task Anything_but_an_exact_listed_name_is_not_served(string path)
    {
        using var client = site.Client();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEqual("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("FICTIONAL TEST FIXTURE", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceRef", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// ASP.NET matches a route's literal segments case-insensitively and ignores a trailing slash, so these reach the
    /// endpoint. What they reach is still only the exact listed file: the file name itself is an ordinal key.
    /// </summary>
    [Theory]
    [InlineData("/Medien/testbild-startseite-960.jpg")]
    [InlineData("/medien/testbild-startseite-960.jpg/")]
    public async Task Route_prefix_variants_reach_only_the_same_exact_listed_file(string path)
    {
        using var client = site.Client();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Fixture("testbild-startseite-960.jpg"), await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task A_post_is_not_served()
    {
        using var client = site.Client();

        using var response = await client.PostAsync("/medien/testbild-startseite-960.jpg", new ByteArrayContent([]));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task With_the_site_disabled_there_is_no_media_endpoint()
    {
        using var factory = new ContentPackFactory(packRoot: null);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/medien/testbild-startseite-960.jpg");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    // ---- Startup refuses an unsafe pack --------------------------------------------------------------

    [Fact]
    public void A_pack_with_a_metadata_bearing_derivative_does_not_start()
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        File.WriteAllBytes(Path.Combine(pack.Root, "media", "testbild-startseite-480.jpg"), WithJpegSegment(Jpeg, 0xE1, ExifPayload()));
        using var factory = new ContentPackFactory(pack.Root);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("'Site:Media:0'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Metadata", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pack_missing_a_listed_derivative_does_not_start()
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        File.Delete(Path.Combine(pack.Root, "media", "testbild-leistung-eins-og.jpg"));
        using var factory = new ContentPackFactory(pack.Root);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("'Site:Media:1'", error.Message, StringComparison.Ordinal);
    }

    // ---- Logging ------------------------------------------------------------------------------------------

    /// <summary>The startup summary gives counts; no request for a photo, listed or not, reaches a log.</summary>
    [Fact]
    public async Task Media_requests_and_startup_log_counts_only()
    {
        var logs = new CapturingLoggerProvider();
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        using var baseFactory = new ContentPackFactory(pack.Root);
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs)));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(MarketingSiteFixture.CanonicalOrigin),
        });

        using (await client.GetAsync("/medien/testbild-startseite-960.jpg")) { }
        using (await client.GetAsync("/medien/sondierung-q8w3.jpg")) { }
        using (await client.GetAsync("/")) { }

        Assert.Contains(logs.Entries, entry => entry.Message.Contains("2 photo(s) in 14 verified file(s)", StringComparison.Ordinal));
        foreach (var secret in new[] { "test-register-0001", "test-register-0002", "sondierung-q8w3", "testbild-startseite", "Testbild mit" })
        {
            Assert.DoesNotContain(logs.Entries, entry => entry.RecordedPartsContain(secret) || entry.ScopesContain(secret));
        }
    }
}
