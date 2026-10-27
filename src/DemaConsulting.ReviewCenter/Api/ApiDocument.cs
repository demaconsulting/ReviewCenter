// Copyright (c) DEMA Consulting
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DemaConsulting.ReviewCenter.Api;

/// <summary>
///     The OpenAPI document of the API: how it is generated and where it is served.
/// </summary>
/// <remarks>
///     The document is generated from the endpoints at run time, so it cannot drift from the code, and a committed
///     snapshot (<c>docs/api/openapi.json</c>) with a test makes every change of the API visible in review. To make
///     the output the same on every machine and build, the version is a fixed string rather than the assembly
///     version, and no server address is written (it would come from the request host). The document contains no
///     secret and is served without a token so that tools can fetch it. The class is stateless and thread-safe.
/// </remarks>
internal static class ApiDocument
{
    /// <summary>
    ///     The address of the document.
    /// </summary>
    public const string Path = ApiEndpoints.Prefix + "/openapi.json";

    /// <summary>
    ///     The name of the bearer security scheme in the document.
    /// </summary>
    public const string SecuritySchemeName = "bearer";

    /// <summary>
    ///     The fixed version written to the document; changes only when the API changes incompatibly.
    /// </summary>
    public const string DocumentVersion = "1";

    /// <summary>
    ///     Registers the generator of the OpenAPI document.
    /// </summary>
    /// <param name="services">The service collection. Must not be null.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddApiDocument(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenApi(options =>
        {
            // Only the API belongs in the document, not the pages, the health check or the sign-in endpoints
            options.ShouldInclude = description =>
                description.RelativePath is not null
                && description.RelativePath.StartsWith("api/", StringComparison.Ordinal);
            options.AddDocumentTransformer((document, _, _) =>
            {
                Describe(document);
                return Task.CompletedTask;
            });
        });
        return services;
    }

    /// <summary>
    ///     Maps the endpoint that serves the OpenAPI document, without authentication.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder. Must not be null.</param>
    /// <returns>The same builder.</returns>
    public static IEndpointRouteBuilder MapApiDocument(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapOpenApi(Path).AllowAnonymous();
        return endpoints;
    }

    /// <summary>
    ///     Writes the fixed parts of the document: title, version, no servers, and the bearer scheme.
    /// </summary>
    /// <param name="document">The generated document.</param>
    private static void Describe(OpenApiDocument document)
    {
        document.Info = new OpenApiInfo
        {
            Title = "ReviewCenter API",
            Version = DocumentVersion,
            Description =
                "Read projects and reviews, create review drafts and download attestations with a personal access "
                + "token (Authorization: Bearer rcpat_...). Reviewer slots and signatures can only be changed in the "
                + "web pages."
        };

        // The server address would come from the request host; leaving it out keeps the document identical everywhere
        document.Servers = [];

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            [SecuritySchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "rcpat",
                Description = "A personal access token created on the Account page."
            }
        };

        // Every operation of the document needs the token
        foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations?.Values.AsEnumerable() ?? []))
        {
            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(SecuritySchemeName, document)] = []
                }
            ];
        }
    }
}
