namespace RenoTrack.Website.PublicApi;

/// <summary>
/// Registers a typed client for the API's customer-facing surface, with everything such a client
/// must have and nothing it may choose to drop.
/// </summary>
/// <remarks>
/// <para>
/// <b>One registration path for every public-API client (D114).</b> Slice 3b added a second typed
/// client, <see cref="IPublicInvoiceClient"/>, beside <see cref="IPublicAngebotClient"/>. Each line
/// below is a security or availability property, and a second hand-written registration is a second
/// place to forget one. The likeliest line to be forgotten is the one that matters most.
/// </para>
/// <list type="bullet">
/// <item><b>Base address and timeout</b> from the validated <see cref="PublicApiOptions"/>, so a
/// hung API costs a customer a bounded wait, never a hung page.</item>
/// <item><b><see cref="ClientAddressForwardingHandler"/></b>, so the API's public rate limiter
/// partitions per customer rather than per Website (D97).</item>
/// <item><b><c>RemoveAllLoggers()</c></b>, which is load-bearing rather than tidiness.
/// <c>IHttpClientFactory</c> attaches its own logging handlers, which write
/// "Sending HTTP request GET {uri}" at Information. Every URI these clients send contains the
/// customer's token. Those handlers log under <c>System.Net.Http.HttpClient.*</c>, not
/// <c>Microsoft.AspNetCore</c>, so the Warning level <c>appsettings.json</c> sets for the latter
/// does not cover them. At the Default level of Information a live credential would be written to
/// every log sink on every page view. They are removed structurally here, so no configuration
/// change can reintroduce them (D97). <c>appsettings.json</c> also pins the category to Warning, as
/// defence in depth. <c>TokenLoggingTests</c> raises that category to Trace for every scenario, so
/// this call is the only thing between the token and the log there.</item>
/// </list>
/// </remarks>
public static class PublicApiClientRegistration
{
    public static IHttpClientBuilder AddPublicApiClient<TClient, TImplementation>(
        this IServiceCollection services,
        PublicApiOptions options)
        where TClient : class
        where TImplementation : class, TClient =>
        services.AddHttpClient<TClient, TImplementation>(client =>
            {
                client.BaseAddress = new Uri($"{options.NormalizedBaseUrl}/");
                client.Timeout = options.Timeout;
            })
            .AddHttpMessageHandler<ClientAddressForwardingHandler>()
            .RemoveAllLoggers();
}
