using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RenoTrack.Website.PublicApi;
using RenoTrack.Website.Security;
using RenoTrack.Website.Tests.PublicApi;

namespace RenoTrack.Website.Tests;

/// <summary>
/// Boots the real RenoTrack.Website with its <b>real</b> typed API client and captures every log
/// entry, for the tests that prove a customer's token reaches no log sink.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not <see cref="CustomerWebsiteFactory"/>.</b> That factory replaces
/// <see cref="IPublicAngebotClient"/> wholesale, which removes the typed client registration in
/// <c>Program.cs</c> — and with it the <c>RemoveAllLoggers()</c> call these tests exist to prove.
/// A test that re-registered the client itself would be testing its own registration. So this
/// factory changes exactly one thing about the client: the innermost network handler is a stub, so
/// no socket is opened. Every handler <c>Program.cs</c> layers above it, and every logging decision
/// it makes, is untouched.
/// </para>
/// <para>
/// <b>Logging is configured by overlaying the shipped <c>appsettings.json</c>, never by replacing
/// it.</b> Overrides are added after the application's own sources, so a level the test does not
/// name is the level production ships. That is what lets these tests fail when the shipped
/// configuration changes. Keys in <paramref name="loggingOverrides"/> are relative to the
/// <c>Logging</c> section, so a provider-specific rule can be written as well as a global one.
/// </para>
/// <para>
/// <b><paramref name="withoutRequestScopeSuppression"/> exists only for the negative control.</b> It
/// removes <see cref="HostingRequestScopeSuppression"/>'s registration, so that control can show the
/// scope leak these tests are built to catch. No other test sets it.
/// </para>
/// </remarks>
internal sealed class TokenLoggingWebsiteFactory(
    Func<HttpRequestMessage, HttpResponseMessage> respond,
    IReadOnlyDictionary<string, string?> loggingOverrides,
    bool withoutRequestScopeSuppression = false) : WebApplicationFactory<Program>
{
    internal const string ApiBaseUrl = "https://api.example.test";

    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // As served: Production, so the /Error handler and the shipped logging levels apply.
        builder.UseEnvironment("Production");

        builder.UseSetting($"{PublicApiOptions.SectionName}:{nameof(PublicApiOptions.BaseUrl)}", ApiBaseUrl);

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(
                loggingOverrides.ToDictionary(pair => $"Logging:{pair.Key}", pair => pair.Value)));

        builder.ConfigureServices(services =>
        {
            if (withoutRequestScopeSuppression)
            {
                var suppression = services.Single(descriptor =>
                    descriptor.ServiceType == typeof(IPostConfigureOptions<LoggerFilterOptions>)
                    && descriptor.ImplementationType == typeof(HostingRequestScopeSuppression));
                services.Remove(suppression);

                // The control reproduces a real leak, so it must not reach a real sink. On Windows the
                // host registers the EventLog provider by default, and without this the control would
                // write its probe token into the machine's Application event log on every run.
                services.RemoveAll<ILoggerProvider>();
            }

            services.AddSingleton<ILoggerProvider>(Logs);

            // Applies to every client the factory builds, so no client name is hard-coded here and
            // the typed client's own registration is not touched. A fresh handler per build, because
            // the factory disposes handler chains when they expire.
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
                    handlerBuilder.PrimaryHandler = new StubHttpMessageHandler(
                        (request, _) => Task.FromResult(respond(request)))));

            services.AddSingleton<IStartupFilter>(new RequestActivityRecorder(this));
        });
    }

    private readonly List<Activity?> _requestActivities = [];

    /// <summary>
    /// The Activity ASP.NET created for each request this host served, or <see langword="null"/>
    /// where it created none. The objects are kept rather than copied, so tags the framework adds when
    /// the Activity stops are visible once the request has completed.
    /// </summary>
    public IReadOnlyList<Activity?> RequestActivities
    {
        get
        {
            lock (_requestActivities)
            {
                return _requestActivities.ToList();
            }
        }
    }

    /// <summary>
    /// Records each request's Activity from the outermost middleware position.
    /// </summary>
    /// <remarks>
    /// <b>Read from the request, never through an <see cref="ActivityListener"/>.</b> A listener
    /// registered here would itself make ASP.NET create activities. That would hide the removal of
    /// <c>RequestActivityTracing</c>, which is exactly what the tracing tests must catch.
    /// </remarks>
    private sealed class RequestActivityRecorder(TokenLoggingWebsiteFactory owner) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                lock (owner._requestActivities)
                {
                    owner._requestActivities.Add(context.Features.Get<IHttpActivityFeature>()?.Activity);
                }

                await nextMiddleware(context);
            });

            next(app);
        };
    }
}
