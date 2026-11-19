using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AcxiomCRM.Web.Infrastructure;

/// <summary>Documents the JWT bearer scheme so Swagger UI can call protected endpoints.</summary>
internal sealed class OpenApiBearerTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "AcxiomCRM API",
            Version = "v1",
            Description = "Secured REST API for customers, leads, opportunities, follow-ups and reports. " +
                          "Obtain a token from POST /api/auth/login and send it as 'Authorization: Bearer {token}'. " +
                          "Errors use RFC 7807 problem details; validation failures return 400 with field errors."
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT access token from /api/auth/login"
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        return Task.CompletedTask;
    }
}
