using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using RenoTrack.Website.Site;

namespace RenoTrack.Website.Tests.Site;

/// <summary>
/// Marketing-page metadata can never sit on a token route — the two independent startup guards (<b>D103</b>).
/// </summary>
public sealed class MarketingPageGuardTests
{
    private static RouteEndpoint Endpoint(string pattern, bool marketing) => new(
        _ => Task.CompletedTask,
        RoutePatternFactory.Parse(pattern),
        order: 0,
        marketing ? new EndpointMetadataCollection(MarketingPageMetadata.Instance) : EndpointMetadataCollection.Empty,
        displayName: pattern);

    [Theory]
    [InlineData("/angebot/{token}")]
    [InlineData("/angebot/{token}/entscheidung/{choice}")]
    [InlineData("/rechnung/{Token}")]
    [InlineData("/x/{TOKEN:minlength(10)}")]
    public void Marketing_metadata_on_a_route_with_a_token_parameter_fails_startup_naming_the_route(string pattern)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            MarketingPageGuard.EnsureNoTokenRoutes([Endpoint("/impressum", marketing: true), Endpoint(pattern, marketing: true)]));

        Assert.Contains($"'{pattern}'", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'/impressum'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Marketing_metadata_without_a_token_parameter_is_accepted()
    {
        MarketingPageGuard.EnsureNoTokenRoutes([Endpoint("/impressum", marketing: true), Endpoint("/leistungen/{slug}", marketing: true)]);
    }

    [Fact]
    public void A_token_route_without_marketing_metadata_is_accepted()
    {
        MarketingPageGuard.EnsureNoTokenRoutes([Endpoint("/angebot/{token}", marketing: false)]);
    }

    [Theory]
    [InlineData("/Angebot")]
    [InlineData("/AngebotDecision")]
    [InlineData("/angebot")]
    public void The_convention_refuses_a_customer_token_page(string pagePath)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            MarketingPageConvention.Apply(new PageConventionCollection(), ["/Impressum", pagePath]));

        Assert.Contains($"'{pagePath}'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_marketing_page_list_contains_no_token_page()
    {
        Assert.DoesNotContain(MarketingPageConvention.PagePaths, path => path.StartsWith("/Angebot", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { "/Impressum", "/Datenschutz", "/NichtGefunden" }, MarketingPageConvention.PagePaths);
    }

    /// <summary>
    /// <b>Not an attribute, by design</b>: no page can declare itself a marketing page, and no token page can
    /// acquire the marker by a stray attribute. Only the startup convention adds it.
    /// </summary>
    [Fact]
    public void The_marker_is_not_an_attribute_and_cannot_be_constructed_elsewhere()
    {
        var type = typeof(MarketingPageMetadata);

        Assert.False(typeof(Attribute).IsAssignableFrom(type));
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors());
    }
}
