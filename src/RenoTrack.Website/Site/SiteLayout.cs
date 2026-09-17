namespace RenoTrack.Website.Site;

/// <summary>Where the marketing layout and its partials live (<b>D103</b>).</summary>
/// <remarks>
/// Full paths, because the partials sit in <c>Pages/Shared/Site/</c>, which Razor's view discovery does not
/// search by name. Keeping them apart from <c>_CustomerLayout</c> keeps the two surfaces from being confused.
/// </remarks>
public static class SiteLayout
{
    public const string Path = "/Pages/Shared/Site/_SiteLayout.cshtml";
    public const string Head = "/Pages/Shared/Site/_SiteHead.cshtml";
    public const string Header = "/Pages/Shared/Site/_SiteHeader.cshtml";
    public const string Footer = "/Pages/Shared/Site/_SiteFooter.cshtml";
    public const string CallBar = "/Pages/Shared/Site/_MobileCallBar.cshtml";

    /// <summary>The customer layout, which a shared page keeps when its endpoint is not a marketing page.</summary>
    public const string CustomerLayout = "_CustomerLayout";

    /// <summary>
    /// The layout a page shared between both surfaces uses: decided from the matched endpoint's metadata
    /// alone, never from configuration.
    /// </summary>
    public static string For(HttpContext context) => context.IsMarketingPage() ? Path : CustomerLayout;
}
