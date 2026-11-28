namespace RenoTrack.Api.ErrorHandling;

/// <summary>
/// Marks an endpoint whose every 409 must leave the API as one generic, user-safe response
/// (<b>D115</b>), whatever caused it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> The Angebot's versioned writes can be refused for three reasons that a
/// caller must not be able to tell apart and must not learn anything from: a stale
/// <c>expectedVersion</c>, an Angebot that is no longer editable, and a lost concurrency race. The
/// first and last already carried a message naming nothing, but the second is the Domain's own
/// <see cref="InvalidOperationException"/>, whose message — rightly, for diagnosis — names the
/// Angebot id and its state ("Cannot edit Angebot 42: status is 'InReview' …"). D59 publishes a
/// mapped exception's message as the ProblemDetails <c>detail</c>, so without this the id and the
/// internal state reached the response.
/// </para>
/// <para>
/// <b>Sanitised at the boundary, not in the Domain or the handler.</b> The Domain keeps its precise
/// message, and <c>ProblemDetailsExceptionHandler</c> still logs the full exception at Warning, so no
/// diagnostic value is lost; only what is <i>published</i> changes. Rewrapping the Domain exception
/// in the Application layer was the alternative, and CLAUDE.md §17 forbids it (Domain guard failures
/// propagate unwrapped).
/// </para>
/// <para>
/// <b>Opt-in per action, and deliberately not on the whole controller.</b> Create, duplicate, send,
/// resend and convert keep their specific 409 messages ("this Lead already has an active Angebot"),
/// which are written for a caller and name what the caller can act on. Only the writes that carry
/// <c>expectedVersion</c> carry this marker.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class GenericConflictResponseAttribute : Attribute
{
    /// <summary>
    /// The one detail every 409 from a marked endpoint publishes. It is true of all three causes and
    /// names no id, table, version or state.
    /// </summary>
    internal const string Detail =
        "This Angebot could not be changed because it is no longer in the state this request was " +
        "based on: it was changed in the meantime or can no longer be edited. Nothing was saved. " +
        "Please reload and try again.";

    private const string ItemsKey = "RenoTrack.GenericConflictResponse";

    /// <summary>
    /// Records whether the matched endpoint carries this marker. Must run while routing metadata
    /// still exists — ASP.NET's exception middleware clears the endpoint before any
    /// <c>IExceptionHandler</c> runs, the same reason <see cref="RouteDiagnostics.Capture"/> exists.
    /// </summary>
    internal static void Capture(HttpContext httpContext)
    {
        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<GenericConflictResponseAttribute>() is not null)
        {
            httpContext.Items[ItemsKey] = true;
        }
    }

    internal static bool AppliesTo(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(ItemsKey, out var applies) && applies is true;
}
