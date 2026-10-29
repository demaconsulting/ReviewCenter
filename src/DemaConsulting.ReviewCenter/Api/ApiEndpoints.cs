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

using System.Globalization;
using System.Text.Json;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;

namespace DemaConsulting.ReviewCenter.Api;

/// <summary>
///     The JSON API under <c>/api</c>: list projects and reviews, read a review, create a review, download the
///     attestation and read the evidence index, all with a personal access token (the attestation and the evidence
///     index can also be read without a token when the project allows it).
/// </summary>
/// <remarks>
///     Every endpoint but two is in one group that requires the <c>Api</c> policy (the token scheme only), so no
///     endpoint can be left open by mistake. The two exceptions, the evidence index and the attestation PDF, are in a
///     second group with their own policies (see <see cref="AnonymousEvidenceHandler"/>): a project can switch each
///     of them open to callers without a token in its evidence settings, and a test enumerates the endpoints so that
///     no third route can join them. The token acts as its owner: the owner's project memberships decide what is visible, and
///     a project the owner may not see is answered with the same 404 as a project that does not exist. The endpoints
///     call the same Core services as the web pages, so the rules (limits,
///     validation, audit trail) are identical. A failed domain rule becomes a problem document through one filter
///     (see <see cref="ApiProblems"/>). Nothing here can sign: reviewer slots and signatures are refused for a token
///     actor by <see cref="SigningService"/>, and no route for them exists. An unknown address under <c>/api</c> is
///     answered with a JSON 404 for every standard HTTP method, also without a token, so the API never falls through
///     to the web pages. The class is
///     stateless and thread-safe.
/// </remarks>
internal static class ApiEndpoints
{
    /// <summary>
    ///     The address prefix of the API.
    /// </summary>
    public const string Prefix = "/api";

    /// <summary>
    ///     The largest request body accepted by the endpoint that creates a review, derived from the review limits: the
    ///     Base64 text of the largest upload (four characters for every three bytes, rounded up), a path (worst case
    ///     six characters per character for <c>\uXXXX</c> escaping) and the JSON framing for every file, and 64 KiB for
    ///     the identifier, the title and the selection. The services stay authoritative for the limits themselves.
    /// </summary>
    internal const long MaxCreateBodyBytes =
        (4 * ((ReviewLimits.MaxTotalSizeBytes + 2) / 3))
        + (ReviewLimits.MaxFileCount * ((ReviewLimits.MaxPathLength * 6L) + 128))
        + (64 * 1024);

    /// <summary>
    ///     The validation field of the list of uploaded files.
    /// </summary>
    private const string FilesField = "files";

    /// <summary>
    ///     The longest Base64 text accepted for one uploaded file: four characters for every three bytes of
    ///     <see cref="ReviewLimits.MaxFileSizeBytes"/>, rounded up. It is checked before decoding; the services check the
    ///     decoded size exactly.
    /// </summary>
    private const long MaxFileBase64Length = 4 * ((ReviewLimits.MaxFileSizeBytes + 2) / 3);

    /// <summary>
    ///     The standard HTTP methods (GET, HEAD, POST, PUT, PATCH, DELETE, OPTIONS) the catch-all route answers with a 404.
    /// </summary>
    private static readonly string[] AllMethods = ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];

    /// <summary>
    ///     The endpoint metadata that raises the request body limit of the endpoint that creates a review.
    /// </summary>
    private sealed class BodySizeLimit : IRequestSizeLimitMetadata
    {
        /// <summary>
        ///     Gets the largest accepted request body in bytes.
        /// </summary>
        public long? MaxRequestBodySize => MaxCreateBodyBytes;
    }

    /// <summary>
    ///     Maps the API endpoints and the JSON 404 for every other address under <c>/api</c>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder. Must not be null.</param>
    /// <returns>The same builder.</returns>
    public static IEndpointRouteBuilder MapApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(Prefix)
            .RequireAuthorization(ApiTokenAuthentication.PolicyName)
            .AddEndpointFilter(DomainRuleFilterAsync);

        // The two routes that a project can open to callers without a token have their own group and their own
        // policies instead of the Api policy; nothing else under /api is reachable without a token
        var open = endpoints.MapGroup(Prefix)
            .AddEndpointFilter(DomainRuleFilterAsync)
            .AddEndpointFilter(AnonymousRateLimitFilterAsync);

        group.MapGet("/projects", ListProjectsAsync)
            .WithName("ListProjects")
            .WithSummary("Lists the projects.")
            .WithDescription(
                "Returns the projects the owner of the token may see (a system administrator sees all, anybody else "
                + "only the projects they are a member of), in the order of the project list of the web pages.")
            .Produces<ProjectDto[]>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/projects/{projectId:int}/reviews", ListReviewsAsync)
            .WithName("ListReviews")
            .WithSummary("Lists the reviews of a project.")
            .WithDescription("Returns the reviews of the project, ordered by their identifier, without their details.")
            .Produces<ReviewSummaryDto[]>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/projects/{projectId:int}/reviews/{reviewId}", GetReviewAsync)
            .WithName("GetReview")
            .WithSummary("Gets a review.")
            .WithDescription("Returns the review with its reviewer slots, approval status, progress counts and next step.")
            .Produces<ReviewDetailDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/projects/{projectId:int}/reviews", CreateReviewAsync)
            .WithName("CreateReview")
            .WithSummary("Creates a review.")
            .WithDescription(
                "Creates a draft review from uploaded files (Base64) or from a registered git repository; send exactly one "
                + "of 'files' and 'repository'. The review is recorded as created with an API token. Reviewer slots and "
                + "signatures cannot be changed with an API token.")
            .WithMetadata(new BodySizeLimit())
            .Accepts<CreateReviewRequest>(isOptional: false, "application/json")
            .Produces<CreatedReviewDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        // A body that is not declared as JSON does not match the route above, which accepts JSON only; this route
        // (with a lower priority than that one, and a higher one than the catch-all below) answers it with 415
        // instead of letting the catch-all answer 404
        group.MapPost("/projects/{projectId:int}/reviews", () => ApiProblems.UnsupportedMediaType())
            .WithOrder(1)
            .ExcludeFromDescription();

        open.MapGet("/projects/{projectId:int}/reviews/{reviewId}/attestation.pdf", GetAttestationAsync)
            .RequireAuthorization(ApiTokenAuthentication.AttestationPolicyName)
            .WithName(ApiDocument.AttestationOperation)
            .WithSummary("Downloads the signed attestation of a complete review.")
            .WithDescription(
                "Returns the signed PDF attestation; the review must be complete (every slot signed). What the PDF "
                + "contains follows the evidence settings of the project. Without a token the request is answered "
                + "only when the project allows anonymous attestation PDFs; otherwise the answer is 401.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        open.MapGet("/projects/{projectId:int}/evidence/index.json", GetEvidenceIndexAsync)
            .RequireAuthorization(ApiTokenAuthentication.EvidenceIndexPolicyName)
            .WithName(ApiDocument.EvidenceIndexOperation)
            .WithSummary("Gets the evidence index of a project, in the format of a ReviewMark index.json.")
            .WithDescription(
                "Returns the completed reviews of the project that were planned from a review-set, one per review-set "
                + "id and fingerprint, as {\"reviews\":[{id, fingerprint, date, result, file}]}. The result is always "
                + "'pass'; 'file' is the absolute address of the signed attestation. Use it as the evidence-source of "
                + "a .reviewmark.yaml file, with a personal access token as the Basic password. Without a token the "
                + "request is answered only when the project allows an anonymous evidence index; otherwise the "
                + "answer is 401.")
            .Produces<EvidenceIndexDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // Every other address under /api answers a JSON 404, with or without a token. A more specific route above wins
        // over this catch-all, so it only sees what nothing else handles
        endpoints.MapMethods(Prefix + "/{**rest}", AllMethods, () => ApiProblems.NotFound("The address does not exist."))
            .WithOrder(2)
            .AllowAnonymous()
            .ExcludeFromDescription();
        return endpoints;
    }

    /// <summary>
    ///     Turns a failed domain rule of any API endpoint into its problem document.
    /// </summary>
    /// <param name="context">The invocation context.</param>
    /// <param name="next">The rest of the endpoint.</param>
    /// <returns>The result of the endpoint, or the problem result of the failed rule.</returns>
    private static async ValueTask<object?> DomainRuleFilterAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (DomainRuleException ex)
        {
            return ApiProblems.ToResult(ex);
        }
    }

    /// <summary>
    ///     Answers 429 to a caller without a token whose address makes too many requests.
    /// </summary>
    /// <param name="context">The invocation context.</param>
    /// <param name="next">The rest of the endpoint.</param>
    /// <returns>The result of the endpoint, or the 429 problem with <c>Retry-After</c>.</returns>
    private static async ValueTask<object?> AnonymousRateLimitFilterAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.User.Identity?.IsAuthenticated != true)
        {
            var limit = http.RequestServices.GetRequiredService<AnonymousRateLimit>();
            if (!limit.TryAcquire(SignIn.ClientAddress(http), out var retryAfter))
            {
                http.Response.Headers.CacheControl = "no-store";
                http.Response.Headers.RetryAfter =
                    Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                return Results.Problem(
                    title: "Too Many Requests",
                    detail: "Too many requests without a token from this address; try again later.",
                    statusCode: StatusCodes.Status429TooManyRequests);
            }
        }

        return await next(context);
    }

    /// <summary>
    ///     Lists the projects the token's owner may see.
    /// </summary>
    /// <param name="http">The request context; supplies the token actor.</param>
    /// <param name="projects">The project service.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The visible projects.</returns>
    private static async Task<Ok<List<ProjectDto>>> ListProjectsAsync(
        HttpContext http,
        ProjectService projects,
        CancellationToken cancellationToken)
    {
        var list = await projects.ListAsync(ApiProblems.ActorOf(http.User), cancellationToken);
        return TypedResults.Ok(list.Select(p => new ProjectDto(p.Id, p.Name, p.Description, p.Created)).ToList());
    }

    /// <summary>
    ///     Lists the reviews of a project.
    /// </summary>
    /// <param name="projectId">The project number.</param>
    /// <param name="http">The request context; supplies the token actor.</param>
    /// <param name="projects">The project service.</param>
    /// <param name="reviews">The review service.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The reviews, or 404 when the project does not exist or is not visible to the token's owner.</returns>
    private static async Task<IResult> ListReviewsAsync(
        int projectId,
        HttpContext http,
        ProjectService projects,
        ReviewService reviews,
        CancellationToken cancellationToken)
    {
        if (await projects.GetAsync(ApiProblems.ActorOf(http.User), projectId, cancellationToken) is null)
        {
            return ApiProblems.NotFound("The project does not exist.");
        }

        var list = await reviews.ListAsync(projectId, cancellationToken);
        return TypedResults.Ok(list.Select(ToSummary).ToList());
    }

    /// <summary>
    ///     Gets one review with its details.
    /// </summary>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="http">The request context; supplies the token actor.</param>
    /// <param name="projects">The project service.</param>
    /// <param name="reviews">The review service.</param>
    /// <param name="signing">The signing service.</param>
    /// <param name="threads">The thread service.</param>
    /// <param name="checklists">The checklist service.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The review, or 404 when it does not exist or its project is not visible to the token's owner.</returns>
    private static async Task<IResult> GetReviewAsync(
        int projectId,
        string reviewId,
        HttpContext http,
        ProjectService projects,
        ReviewService reviews,
        SigningService signing,
        ThreadService threads,
        ChecklistService checklists,
        CancellationToken cancellationToken)
    {
        if (await projects.GetAsync(ApiProblems.ActorOf(http.User), projectId, cancellationToken) is null)
        {
            return ApiProblems.NotFound("The project does not exist.");
        }

        var review = await reviews.GetAsync(projectId, reviewId, cancellationToken);
        if (review is null)
        {
            return ApiProblems.NotFound("The review does not exist.");
        }

        var detail = await BuildDetailAsync(review, signing, threads, checklists, cancellationToken);
        return TypedResults.Ok(detail);
    }

    /// <summary>
    ///     Creates a review from uploaded files or from a repository.
    /// </summary>
    /// <param name="projectId">The project number.</param>
    /// <param name="http">The request context; supplies the body and the token actor.</param>
    /// <param name="projects">The project service.</param>
    /// <param name="reviews">The review service.</param>
    /// <param name="signing">The signing service.</param>
    /// <param name="threads">The thread service.</param>
    /// <param name="checklists">The checklist service.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    ///     201 with the review and its address; a problem document when the body is not JSON (415), is not JSON of the
    ///     right shape (400), or is not valid. A body over the size limit of the endpoint is answered 413 by the server.
    /// </returns>
    private static async Task<IResult> CreateReviewAsync(
        int projectId,
        HttpContext http,
        ProjectService projects,
        ReviewService reviews,
        SigningService signing,
        ThreadService threads,
        ChecklistService checklists,
        CancellationToken cancellationToken)
    {
        // Whoever may not see the project gets the answer for a missing one, before anything of the body is read
        var actor = ApiProblems.ActorOf(http.User);
        if (await projects.GetAsync(actor, projectId, cancellationToken) is null)
        {
            return ApiProblems.NotFound("The project does not exist.");
        }

        // The body is read here, not bound by the framework, so that a body that is not JSON of the right shape
        // answers a problem document like every other refusal. The route only matches a JSON content type
        CreateReviewRequest? request;
        try
        {
            request = http.Request.ContentLength is 0
                ? null
                : await http.Request.ReadFromJsonAsync<CreateReviewRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            // The text of the parser is not echoed: it names positions and types of the caller's own body only
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = ["The request body is not valid JSON for a review."]
            });
        }

        // Exactly one source: the request itself is checked here; every other rule belongs to the services
        var errors = CheckSource(request);
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        Review review;
        IReadOnlyList<SkippedFileDto> skipped = [];
        if (request!.Files is not null)
        {
            var files = DecodeFiles(request.Files, out var decodeErrors);
            if (decodeErrors is not null)
            {
                // The detail repeats the first message, as the problem of a failed limit in the services does it
                return Results.ValidationProblem(decodeErrors, detail: decodeErrors.Values.First()[0]);
            }

            review = await reviews.CreateDraftAsync(projectId, request.Id, request.Title, files, actor, cancellationToken);
        }
        else
        {
            var source = request.Repository!;
            var result = await reviews.CreateFromRepositoryAsync(
                projectId,
                request.Id,
                request.Title,
                source.RepositoryId,
                source.HeadRevision,
                source.BaselineRevision,
                source.Selection,
                actor,
                cancellationToken);
            review = result.Review;
            skipped = result.Skipped.Select(s => new SkippedFileDto(s.Path, s.Reason)).ToList();
        }

        var detail = await BuildDetailAsync(review, signing, threads, checklists, cancellationToken);
        var location = $"{Prefix}/projects/{projectId}/reviews/{Uri.EscapeDataString(review.Id)}";
        return TypedResults.Created(location, new CreatedReviewDto(detail, skipped));
    }

    /// <summary>
    ///     Downloads the signed attestation of a review.
    /// </summary>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="http">The request context.</param>
    /// <param name="attestations">The attestation service.</param>
    /// <returns>The PDF, or the problem document of the status.</returns>
    private static async Task<IResult> GetAttestationAsync(
        int projectId,
        string reviewId,
        HttpContext http,
        AttestationService attestations)
    {
        var result = await attestations.GenerateAsync(
            projectId,
            reviewId,
            ApiProblems.ActorOrAnonymous(http.User),
            http.RequestAborted);
        return AttestationEndpoint.ToResult(result, http, () => ApiProblems.NotFound("The review does not exist."));
    }

    /// <summary>
    ///     Gets the evidence index of a project.
    /// </summary>
    /// <param name="projectId">The project number.</param>
    /// <param name="http">The request context; supplies the address the attestation links are built from.</param>
    /// <param name="projects">The project service.</param>
    /// <param name="index">The evidence index service.</param>
    /// <returns>The index, or 404 when the project does not exist.</returns>
    private static async Task<IResult> GetEvidenceIndexAsync(
        int projectId,
        HttpContext http,
        ProjectService projects,
        EvidenceIndexService index)
    {
        // The index holds the addresses of signed documents behind a token: never cache any response of it in a
        // shared cache, including the not-found problem
        http.Response.Headers.CacheControl = "no-store";
        var actor = ApiProblems.ActorOrAnonymous(http.User);

        // Without a token the index service answers only for a project that allows it, and refuses with the same
        // not-found as a project that does not exist
        if (actor is not null && await projects.GetAsync(actor, projectId, http.RequestAborted) is null)
        {
            return ApiProblems.NotFound("The project does not exist.");
        }

        var entries = await index.BuildAsync(actor, projectId, http.RequestAborted);
        return TypedResults.Ok(new EvidenceIndexDto(
            entries.Select(entry => new EvidenceEntryDto(
                entry.Id,
                entry.Fingerprint,
                entry.Date,
                "pass",
                AttestationUrl(http.Request, projectId, entry.ReviewId))).ToList()));
    }

    /// <summary>
    ///     Builds the absolute address of the attestation of a review from the address of the request.
    /// </summary>
    /// <param name="request">The request; supplies the scheme, the host and the path base.</param>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <returns>The address, for example <c>https://host/api/projects/1/reviews/R-1/attestation.pdf</c>.</returns>
    /// <remarks>
    ///     The scheme follows the forwarded protocol when the forwarded headers are enabled; the host is the
    ///     <c>Host</c> header of the request, so a reverse proxy must pass it on.
    /// </remarks>
    internal static string AttestationUrl(HttpRequest request, int projectId, string reviewId)
    {
        return $"{request.Scheme}://{request.Host}{request.PathBase}{Prefix}/projects/{projectId}"
            + $"/reviews/{Uri.EscapeDataString(reviewId)}/attestation.pdf";
    }

    /// <summary>
    ///     Checks that a request names exactly one source.
    /// </summary>
    /// <param name="request">The request, or <c>null</c> when the body was empty.</param>
    /// <returns>The validation errors by field, or <c>null</c> when the request is well formed.</returns>
    private static Dictionary<string, string[]>? CheckSource(CreateReviewRequest? request)
    {
        if (request is null)
        {
            return new Dictionary<string, string[]> { ["body"] = ["A JSON request body is required."] };
        }

        if (request.Files is null == request.Repository is null)
        {
            return new Dictionary<string, string[]>
            {
                ["source"] = ["Send exactly one of 'files' and 'repository'."]
            };
        }

        return null;
    }

    /// <summary>
    ///     Decodes the Base64 content of uploaded files.
    /// </summary>
    /// <remarks>
    ///     The request body is already capped, but the number of files and the size of each one come from the caller, so
    ///     they are bounded here before anything is allocated or decoded: more than
    ///     <see cref="ReviewLimits.MaxFileCount"/> files, or a Base64 text longer than <see cref="MaxFileBase64Length"/>
    ///     (the Base64 length of <see cref="ReviewLimits.MaxFileSizeBytes"/>), is a validation error. The services stay
    ///     authoritative for the exact limits (they still check the size of every decoded file and the total).
    /// </remarks>
    /// <param name="files">The files of the request.</param>
    /// <param name="errors">The validation errors by field, or <c>null</c> when every file could be decoded.</param>
    /// <returns>The files with their bytes.</returns>
    private static List<UploadedFile> DecodeFiles(
        IReadOnlyList<UploadFileDto> files,
        out Dictionary<string, string[]>? errors)
    {
        // The count is checked before the list is created or walked: it is the caller's number, not a trusted one
        var decoded = new List<UploadedFile>();
        errors = null;
        if (files.Count > ReviewLimits.MaxFileCount)
        {
            errors = new Dictionary<string, string[]>
            {
                [FilesField] = [$"A review can have at most {ReviewLimits.MaxFileCount} files."]
            };
            return decoded;
        }

        // First pass: every text is measured before any of them is decoded, so an oversized entry never allocates
        // its bytes and no earlier entry is decoded and retained just to be thrown away
        for (var index = 0; index < files.Count; index++)
        {
            if (files[index]?.ContentBase64 is { } text && text.Length > MaxFileBase64Length)
            {
                // Only the position is named: the content itself is never echoed
                errors = new Dictionary<string, string[]>
                {
                    [$"files[{index}].contentBase64"] =
                        [$"The content is larger than the limit of {ReviewLimits.MaxFileSizeBytes / 1024 / 1024} MiB."]
                };
                return decoded;
            }
        }

        // Second pass: decode, now that every text is known to be within the bound
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            try
            {
                decoded.Add(new UploadedFile(file?.Path ?? string.Empty, Convert.FromBase64String(file?.ContentBase64 ?? string.Empty)));
            }
            catch (FormatException)
            {
                // Only the position is named: the content itself is never echoed
                errors = new Dictionary<string, string[]>
                {
                    [$"files[{index}].contentBase64"] = ["The content is not valid Base64."]
                };
                return decoded;
            }
        }

        return decoded;
    }

    /// <summary>
    ///     Maps a review to its list entry.
    /// </summary>
    /// <param name="review">The review.</param>
    /// <returns>The list entry.</returns>
    private static ReviewSummaryDto ToSummary(Review review)
    {
        return new ReviewSummaryDto(
            review.Id,
            review.Title,
            review.State.ToString(),
            review.Created,
            review.ContentFingerprint,
            review.CreatedBy,
            review.CreatedViaApi,
            review.IsFromRepository);
    }

    /// <summary>
    ///     Builds the details of a review from the services that own each part.
    /// </summary>
    /// <param name="review">The review, with its files.</param>
    /// <param name="signing">The signing service.</param>
    /// <param name="threads">The thread service.</param>
    /// <param name="checklists">The checklist service.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The details.</returns>
    private static async Task<ReviewDetailDto> BuildDetailAsync(
        Review review,
        SigningService signing,
        ThreadService threads,
        ChecklistService checklists,
        CancellationToken cancellationToken)
    {
        var approval = await signing.GetApprovalAsync(review.ProjectId, review.Id, cancellationToken);
        var threadList = await threads.ListAsync(review.ProjectId, review.Id, cancellationToken);
        var checklist = await checklists.GetAsync(review.ProjectId, review.Id, cancellationToken);

        // The checklist counts follow the screen: only items of applicable sections count
        var items = 0;
        var answered = 0;
        var failed = 0;
        if (checklist is not null)
        {
            items = checklist.Overall.Total;
            answered = checklist.Overall.Answered;
            failed = checklist.Definition.Sections
                .Where(section => checklist.IsApplicable(section.Id))
                .SelectMany(section => section.Items)
                .Count(item => checklist.AnswerFor(item.Id)?.Outcome == ChecklistOutcome.Fail);
        }

        var counts = new ReviewCountsDto(
            review.Files.Count,
            threadList.Count(t => t.State == ReviewThreadState.Open),
            threadList.Count(t => t.State == ReviewThreadState.Resolved),
            items,
            answered,
            failed);
        var slots = approval?.Slots.Select(s => new SlotDto(
            s.SlotId,
            s.Number,
            s.Primary.Username,
            s.Alternate?.Username,
            s.State.ToString(),
            s.SignerName,
            s.SignedAt)).ToList() ?? [];
        return new ReviewDetailDto(
            review.Id,
            review.Title,
            review.State.ToString(),
            review.Created,
            review.ContentFingerprint,
            review.CreatedBy,
            review.CreatedViaApi,
            review.IsFromRepository,
            review.RepositoryName,
            review.BaselineSha,
            review.HeadSha,
            review.Selection,
            review.ReviewSetId,
            approval?.IsApproved ?? false,
            approval?.Digest ?? string.Empty,
            slots,
            counts,
            approval?.Blockers ?? [],
            approval?.NextStep ?? string.Empty);
    }
}
