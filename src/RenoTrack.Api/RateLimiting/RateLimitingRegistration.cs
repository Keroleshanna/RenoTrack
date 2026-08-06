using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace RenoTrack.Api.RateLimiting;

/// <summary>
/// Registers the single public-surface rate-limiting policy (D65).
/// </summary>
public static class RateLimitingRegistration
{
    public static IServiceCollection AddPublicRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // Eagerly validated and registered as a Singleton, matching every other options type here:
        // a nonsensical limit must fail startup naming the key rather than take the customer-facing
        // surface offline at the first request.
        var options = configuration.GetSection(PublicRateLimitOptions.SectionName).Get<PublicRateLimitOptions>()
            ?? new PublicRateLimitOptions();
        options.Validate();
        services.AddSingleton(options);

        services.AddRateLimiter(limiter =>
        {
            // A *named policy*, applied by [EnableRateLimiting] on PublicController, rather than a
            // GlobalLimiter. That is what keeps authenticated and internal routes out of it: an
            // endpoint is covered only by opting in, so a future controller cannot inherit the
            // public allowance by accident.
            limiter.AddPolicy(PublicRateLimitOptions.PolicyName, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PublicRateLimitPartition.KeyFor(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = options.Window,

                        // No queue: a rejected caller is told to come back, never parked holding a
                        // server resource. Queueing would let an abusive client consume capacity
                        // precisely by exceeding the limit.
                        QueueLimit = 0,
                    }));

            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                // The limiter reports how long the current window has left when it can; the
                // configured window is the correct upper bound when it cannot.
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var metadata)
                    ? metadata
                    : options.Window;
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                // Written through IProblemDetailsService, not hand-serialised, so a 429 obeys the
                // same RFC 7807 contract as every other error (CLAUDE.md §22) and passes through
                // CustomizeProblemDetails — which is what adds traceId and, critically, sets
                // `instance` from RouteDiagnostics so a throttled token request cannot leak the
                // token that was throttled.
                var problemDetailsService = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too Many Requests",
                        Detail = "Too many requests. Please wait a moment and try again.",
                    },
                });
            };
        });

        return services;
    }
}
