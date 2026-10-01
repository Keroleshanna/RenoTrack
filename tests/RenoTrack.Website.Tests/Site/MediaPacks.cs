using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RenoTrack.Website.Tests.Site;

/// <summary>
/// Variants of the fictional Alpha pack for the photo-coverage cases (<b>D106</b>): the fixture itself has a hero photo
/// and one of two services photographed — the shape of the real first deployment.
/// </summary>
internal sealed class MediaPack : IDisposable
{
    private readonly TemporaryContentPack pack;

    private MediaPack(Action<JsonObject> edit)
    {
        pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        var path = Path.Combine(pack.Root, "site.json");
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(json["Site"]!.AsObject());
        File.WriteAllText(path, json.ToJsonString());
        Factory = new ContentPackFactory(pack.Root);
    }

    internal ContentPackFactory Factory { get; }

    internal string Root => pack.Root;

    internal HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri(MarketingSiteFixture.CanonicalOrigin),
    });

    /// <summary>Every service photographed: the only case in which cards show photos (S5-7).</summary>
    internal static MediaPack AllServicesPhotographed() => new(site =>
    {
        site["Media"]!.AsArray().Add(new JsonObject
        {
            ["Id"] = "testbild-leistung-zwei",
            ["Alt"] = "Drittes Testbild mit violetten Streifen (Testdaten)",
            ["SourceRef"] = "test-register-0003",
        });
        site["Services"]![1]!["Image"] = "testbild-leistung-zwei";
    });

    /// <summary>No photos at all: every page exactly as in Slice 4.</summary>
    internal static MediaPack NoPhotos() => new(site =>
    {
        site.Remove("Media");
        site["Home"]!.AsObject().Remove("HeroImage");
        site["Services"]![0]!.AsObject().Remove("Image");
    });

    /// <summary>Any edit, for a single test's case.</summary>
    internal static MediaPack With(Action<JsonObject> edit) => new(edit);

    public void Dispose()
    {
        Factory.Dispose();
        pack.Dispose();
    }
}
