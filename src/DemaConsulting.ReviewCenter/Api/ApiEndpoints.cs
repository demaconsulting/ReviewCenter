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

using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Evidence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;

namespace DemaConsulting.ReviewCenter.Api;

/// <summary>
///     The JSON API under <c>/api</c>: list projects and reviews, read a review, create a review and download the
///     attestation, all with a personal access token.
/// </summary>
/// <remarks>
///     Every endpoint is in one group that requires the <c>Api</c> policy (the token scheme only), so no endpoint can
///     be left open by mistake. The endpoints call the same Core services as the web pages, so the rules (limits,
///     validation, audit trail) are identical. A failed domain rule becomes a problem document through one filter
///     (see <see cref="ApiProblems"/>). Nothing here can sign: reviewer slots and signatures are refused for a token
///     actor by <see cref="SigningService"/>, and no route for them exists. An unknown address under <c>/api</c> is
///     answered with a JSON 404, also without a token, so the API never falls through to the web pages. The class is
///     stateless and thread-safe.
/// </remarks>
internal static class ApiEndpoints
{
    /// <summary>
    ///     The address prefix of the API.
    /// </summary>
    public const string Prefix = "/api";

    /// <summary>
    ///     The largest request body accepted by the endpoint that creates a review: the largest upload
    ///     (20 MiB) as Base64 text plus the JSON around it.
    /// </summary>
    private const long MaxCreateBodyBytes = 30L * 1024 * 1024;

    /// <summary>
    ///     The HTTP methods the catch-all route answers with a 404.
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

        group.MapGet("/projects", ListProjectsAsync)
            .WithName("ListProjects")
            .WithSummary("Lists the projects.")
            .WithDescription("Returns every project, in the order of the project list of the web pages.")
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
            .Produces<CreatedReviewDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        group.MapGet("/projects/{projectId:int}/reviews/{reviewId}/attestation.pdf", GetAttestationAsync)
            .WithName("GetAttestation")
            .WithSummary("Downloads the signed attestation of a complete review.")
            .WithDescription("Returns the signed PDF attestation; the review must be complete (every slot signed).")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Every other address under /api answers a JSON 404, with or without a token. A more specific route above wins
        // over this catch-all, so it only sees what nothing else handles
        endpoints.MapMethods(Prefix + "/{**rest}", AllMethods, () => ApiProblems.NotFound("The address does not exist."))
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
    ///     Lists the projects.
    /// </summary>
    /// <param name="projects">The project service.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The projects.</returns>
    private static async Task<Ok<List<ProjectDto>>> ListProjectsAsync(
        ProjectService projects,
        CancellationToken cancellationToken)
    {
        var list = await projects.ListAsync(cancellationToken);
        return TypedResults.Ok(list.Select(p => new ProjectDto(p.Id, p.Name, p.Description, p.Created)).ToList());
    }

    /// <summary>
    ///     Lists the reviews of a project.
    /// </summary>
    /// <param name="projectId">The project number.</param>
    /// <param name="projects">The project service.</param>
    /// <param name="reviews">The review service.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The reviews, or 404 when the project does not exist.</returns>
    private static async Task<IResult> ListReviewsAsync(
        int projectId,
        ProjectService projects,
        ReviewService reviews,
        CancellationToken cancellationToken)
    {
        if (await projects.GetAsync(projectId, cancellationToken) is null)
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
    /// <param name="reviews">The review service.</param>
    /// <param name="signing">The signing service.</param>
    /// <param name="threads">The thread service.</param>
    /// <param name="checklists">The checklist service.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The review, or 404 when it does not exist.</returns>
    private static async Task<IResult> GetReviewAsync(
        int projectId,
        string reviewId,
        ReviewService reviews,
        SigningService signing,
        ThreadService threads,
        ChecklistService checklists,
        CancellationToken cancellationToken)
    {
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
    /// <param name="request">The request body; <c>null</c> when it is empty.</param>
    /// <param name="http">The request context; supplies the token actor.</param>
    /// <param name="projects">The project service.</param>
    /// <param name="reviews">The review service.</param>
    /// <param name="signing">The signing service.</param>
    /// <param name="threads">The thread service.</param>
    /// <param name="checklists">The checklist service.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the review and its address; a problem document when the request is not valid.</returns>
    private static async Task<IResult> CreateReviewAsync(
        int projectId,
        CreateReviewRequest? request,
        HttpContext http,
        ProjectService projects,
        ReviewService reviews,
        SigningService signing,
        ThreadService threads,
        ChecklistService checklists,
        CancellationToken cancellationToken)
    {
        // Exactly one source: the request itself is checked here; every other rule belongs to the services
        var errors = CheckSource(request);
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        if (await projects.GetAsync(projectId, cancellationToken) is null)
        {
            return ApiProblems.NotFound("The project does not exist.");
        }

        var actor = ApiProblems.ActorOf(http.User);
        Review review;
        IReadOnlyList<SkippedFileDto> skipped = [];
        if (request!.Files is not null)
        {
            var files = DecodeFiles(request.Files, out var decodeErrors);
            if (decodeErrors is not null)
            {
                return Results.ValidationProblem(decodeErrors);
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
            ApiProblems.ActorOf(http.User),
            http.RequestAborted);
        return AttestationEndpoint.ToResult(result, http, () => ApiProblems.NotFound("The review does not exist."));
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
    /// <param name="files">The files of the request.</param>
    /// <param name="errors">The validation errors by field, or <c>null</c> when every file could be decoded.</param>
    /// <returns>The files with their bytes.</returns>
    private static List<UploadedFile> DecodeFiles(
        IReadOnlyList<UploadFileDto> files,
        out Dictionary<string, string[]>? errors)
    {
        var decoded = new List<UploadedFile>(files.Count);
        errors = null;
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
