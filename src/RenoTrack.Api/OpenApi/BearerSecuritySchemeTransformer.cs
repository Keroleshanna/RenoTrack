using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RenoTrack.Api.OpenApi;

/// <summary>
/// Declares the JWT bearer security scheme in the generated OpenAPI document, so the API
/// documentation UI offers an "Authorize" affordance and every protected endpoint added from
/// Slice 4 onward is exercisable by hand, not only through RenoTrack.Api.Tests.
/// </summary>
/// <remarks>
/// This transformer only <em>describes</em> the scheme — it neither registers an authentication
/// handler nor enforces anything. Authentication itself (JWT issuance and validation) is Phase 4
/// Slice 4's deliverable. Until that lands, the document advertises a scheme the API does not yet
/// enforce; this is a deliberate, temporary consequence of establishing the documentation surface
/// in the foundation slice, recorded here so it is not mistaken for a security gap.
/// </remarks>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description =
                "JWT bearer token issued by POST /api/v1/auth/login. Supply as: Authorization: Bearer {token}",
        };

        return Task.CompletedTask;
    }
}
