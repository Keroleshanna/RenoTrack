using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Application.Common.Exceptions;

namespace RenoTrack.Api.ErrorHandling;

/// <summary>
/// Turns every exception escaping a command/query handler into an RFC 7807 ProblemDetails
/// response (Architecture.md §5.3), so controllers never need a try/catch and every client sees
/// one consistent error shape.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a single handler containing one explicit switch rather than one
/// <see cref="IExceptionHandler"/> per exception type. Chained handlers would make registration
/// order silently determine behavior and scatter the mapping across six files; keeping the whole
/// table in one readable block is the same reasoning that made <c>AuditAction</c> one shared enum
/// rather than several per-entity ones (D24).
/// </para>
/// <para>
/// <b>Message-leakage policy.</b> Mapped exceptions carry their message outward, because every one
/// of them is authored in this codebase and phrased for a caller (e.g. BR-10's "Cannot add a photo
/// to an Inspection completed at ..."). Anything unmapped gets a fixed generic message and no
/// detail — an unexpected SqlException must never surface connection strings or schema names. That
/// distinction is the entire reason the fallback is a separate branch rather than shared code.
/// </para>
/// <para>
/// <b>Known, accepted risk (D59).</b> <see cref="ArgumentException"/> and
/// <see cref="InvalidOperationException"/> are BCL-wide types, not ours. EF Core throws
/// InvalidOperationException for tracking conflicts and untranslatable queries, so a genuine
/// infrastructure fault could be reported as 409 Conflict instead of 500. This was reviewed and
/// accepted: today every request-path occurrence of both types originates in a Domain guard
/// (Infrastructure's two InvalidOperationException throws are startup-only — DI configuration and
/// role seeding — and can never occur during a request). The mitigation is that every mapped
/// exception is logged at Warning with its full stack trace, so a masked infrastructure bug is
/// still discoverable in logs rather than invisible. Revisit only with concrete evidence of a real
/// masking incident, not on principle.
/// </para>
/// </remarks>
internal sealed class ProblemDetailsExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ProblemDetailsExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = Map(exception);

        if (problemDetails.Status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception processing {Method} {Route}.",
                httpContext.Request.Method,
                RouteOf(httpContext));
        }
        else
        {
            // Warning, with the full exception: a mapped status is an expected outcome, but the
            // stack trace is what makes an incorrectly-mapped infrastructure fault findable.
            logger.LogWarning(
                exception,
                "Request {Method} {Route} failed with {StatusCode}.",
                httpContext.Request.Method,
                RouteOf(httpContext),
                problemDetails.Status);
        }

        httpContext.Response.StatusCode = problemDetails.Status!.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails,
        });
    }

    /// <summary>
    /// The matched route <em>template</em> (<c>api/v1/public/angebote/{token}</c>), never the raw
    /// path.
    ///
    /// <b>Changed in Phase 6 Slice 3 after the raw path was found to write a live credential into
    /// every log sink.</b> Until then the public token link was the first URL in this system whose
    /// path segment is itself a secret — logging <c>{Path}</c> put customer tokens into
    /// application logs, where they persist and are far more widely readable than the request that
    /// carried them. Nothing is lost by generalising: the id-bearing exceptions already put their
    /// key in the message, which is logged alongside, and a template aggregates better across
    /// requests than a path full of distinct ids.
    ///
    /// Falls back to the raw path only when no endpoint matched, which cannot be a token route —
    /// an unmatched request never reached a handler that could throw.
    /// </summary>
    private static string RouteOf(HttpContext httpContext) =>
        (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
        ?? httpContext.Request.Path.ToString();

    private static ProblemDetails Map(Exception exception) => exception switch
    {
        NotFoundException => Problem(StatusCodes.Status404NotFound, "Not Found", exception.Message),

        ForbiddenException => Problem(StatusCodes.Status403Forbidden, "Forbidden", exception.Message),

        ConflictException => Problem(StatusCodes.Status409Conflict, "Conflict", exception.Message),

        // Phase 6: an expired customer token link. Sequence Diagram §6 names 410 explicitly, and
        // §12 requires the specific reason — folding it into the 404 above would contradict both.
        GoneException => Problem(StatusCodes.Status410Gone, "Gone", exception.Message),

        // Field-keyed errors rather than a flat string: the Dashboard (Phase 10) needs to highlight
        // individual inputs, and this is the same shape ASP.NET already emits for model-binding
        // failures — two different shapes for the same class of problem would be worse than one.
        ValidationException validationException => new ValidationProblemDetails(
            validationException.Errors
                .GroupBy(failure => failure.PropertyName)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(failure => failure.ErrorMessage).ToArray()))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation Failed",
        },

        // Domain guard failures. See the "known, accepted risk" note above (D59).
        ArgumentException => Problem(StatusCodes.Status400BadRequest, "Bad Request", exception.Message),

        InvalidOperationException => Problem(StatusCodes.Status409Conflict, "Conflict", exception.Message),

        // Never echo an unmapped exception's message — it may carry internal detail.
        _ => Problem(
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred.",
            detail: null),
    };

    private static ProblemDetails Problem(int status, string title, string? detail) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
    };
}
