using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ReadNest.Web.Api;

/// <summary>Documents the Identity bearer token so authenticated endpoints can be tried from the API reference UI.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "ReadNest API";
        document.Info.Description =
            "Personal library and reading tracker API. Obtain a token with POST /api/auth/login (body: email, password) " +
            "and send it as 'Authorization: Bearer <accessToken>'. Browser sessions (Identity cookie) are also accepted.";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Description = "Access token returned by POST /api/auth/login.",
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
        });
        return Task.CompletedTask;
    }
}
