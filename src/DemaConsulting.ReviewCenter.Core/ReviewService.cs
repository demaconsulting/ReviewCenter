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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     Creates and reads reviews, enforcing the review rules and computing the content fingerprint.
/// </summary>
/// <remarks>
///     The service is the single place where review rules live. A review is created in the
///     <see cref="ReviewState.Draft"/> state from a set of uploaded files; the bytes are stored
///     verbatim and the fingerprint is computed with <see cref="Fingerprint"/>. It holds no state
///     of its own and is safe for concurrent use as long as the injected <see cref="IReviewStore"/> is.
/// </remarks>
public sealed class ReviewService
{
    /// <summary>
    ///     The store that persists reviews.
    /// </summary>
    private readonly IReviewStore _store;

    /// <summary>
    ///     The clock that supplies creation times.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ReviewService"/> class.
    /// </summary>
    /// <param name="store">The store that persists reviews. Must not be null.</param>
    /// <param name="timeProvider">The clock used for creation times; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ReviewService(IReviewStore store, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Creates a review in the draft state from a set of files.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="id">
    ///     The review identifier: 1 to <see cref="ReviewLimits.MaxReviewIdLength"/> characters from
    ///     letters, digits, dots, underscores and hyphens, starting with a letter or digit. Must be
    ///     unique within the project (other projects may use the same id).
    /// </param>
    /// <param name="title">
    ///     The title; leading and trailing white space is removed. Must not be empty and must not
    ///     exceed <see cref="ReviewLimits.MaxReviewTitleLength"/> characters.
    /// </param>
    /// <param name="files">
    ///     The files of the review. Must not be null. Must contain at least one file, at most
    ///     <see cref="ReviewLimits.MaxFileCount"/>; paths must be non-empty, at most
    ///     <see cref="ReviewLimits.MaxPathLength"/> characters, and unique (ordinal comparison); each file at most
    ///     <see cref="ReviewLimits.MaxFileSizeBytes"/> and all together at most
    ///     <see cref="ReviewLimits.MaxTotalSizeBytes"/> bytes.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The created review, including its fingerprint and file metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="files"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when any rule is violated (see <see cref="DomainRule"/>); nothing is stored then.
    /// </exception>
    public async Task<Review> CreateDraftAsync(
        int projectId,
        string? id,
        string? title,
        IReadOnlyList<UploadedFile> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        // Validate everything that needs no I/O first so a bad request fails fast and cheaply
        ValidateId(id);
        var trimmedTitle = ValidateTitle(title);
        ValidateFiles(files);

        // The project must exist, and the id must be free within it. The store backs the id check up
        // with a uniqueness constraint, which covers two requests racing past this check
        if (await _store.GetProjectAsync(projectId, cancellationToken) is null)
        {
            throw new DomainRuleException(DomainRule.ProjectNotFound, $"Project {projectId} does not exist.");
        }

        if (await _store.ReviewExistsAsync(projectId, id!, cancellationToken))
        {
            throw new DomainRuleException(
                DomainRule.ReviewIdDuplicate,
                $"A review with the id '{id}' already exists in this project.");
        }

        // Keep the bytes verbatim and record the size and hash of each file alongside them
        var reviewFiles = files
            .Select(file => new ReviewFile
            {
                ProjectId = projectId,
                ReviewId = id!,
                Path = file.Path,
                Content = file.Content,
                Size = file.Content.Length,
                Sha256 = Fingerprint.ComputeFileHash(file.Content)
            })
            .ToList();

        // The fingerprint covers the file hashes only; see Fingerprint for the exact algorithm
        var review = new Review
        {
            ProjectId = projectId,
            Id = id!,
            Title = trimmedTitle,
            State = ReviewState.Draft,
            Created = _timeProvider.GetUtcNow(),
            ContentFingerprint = Fingerprint.Compute(reviewFiles.Select(file => file.Sha256)),
            Files = reviewFiles,

            // The checklist is frozen now: later changes of the default never alter this review's checks
            Checklist = new ReviewChecklist
            {
                ProjectId = projectId,
                ReviewId = id!,
                Version = DefaultChecklist.Version,
                Definition = DefaultChecklist.Current.ToJson()
            }
        };

        await _store.AddReviewAsync(review, cancellationToken);
        return review;
    }

    /// <summary>
    ///     Reads one review with its file metadata.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The review with its files ordered by path (no file bytes), or <c>null</c> when it does not exist.</returns>
    public Task<Review?> GetAsync(int projectId, string reviewId, CancellationToken cancellationToken = default)
    {
        return _store.GetReviewAsync(projectId, reviewId, cancellationToken);
    }

    /// <summary>
    ///     Lists the reviews of a project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The reviews ordered by id, without files; empty when there are none.</returns>
    public Task<IReadOnlyList<Review>> ListAsync(int projectId, CancellationToken cancellationToken = default)
    {
        return _store.ListReviewsAsync(projectId, cancellationToken);
    }

    /// <summary>
    ///     Reads the exact bytes of one file of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="path">The file path within the review.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The bytes exactly as uploaded, or <c>null</c> when the file does not exist.</returns>
    public Task<byte[]?> GetFileContentAsync(
        int projectId,
        string reviewId,
        string path,
        CancellationToken cancellationToken = default)
    {
        return _store.GetFileContentAsync(projectId, reviewId, path, cancellationToken);
    }

    /// <summary>
    ///     Checks that a review id is acceptable.
    /// </summary>
    /// <param name="id">The candidate id.</param>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ReviewIdInvalid"/> when the id is not acceptable.</exception>
    private static void ValidateId(string? id)
    {
        // The id appears in page addresses, so only characters that are safe there are accepted
        if (string.IsNullOrEmpty(id) || id.Length > ReviewLimits.MaxReviewIdLength || !IsAllowedId(id))
        {
            throw new DomainRuleException(
                DomainRule.ReviewIdInvalid,
                $"The review id must be 1 to {ReviewLimits.MaxReviewIdLength} characters: letters, digits, '.', '_' " +
                "and '-', starting with a letter or digit.");
        }
    }

    /// <summary>
    ///     Reports whether every character of a review id is allowed and the first is a letter or digit.
    /// </summary>
    /// <param name="id">The candidate id; not empty.</param>
    /// <returns><c>true</c> when the id has the allowed form.</returns>
    private static bool IsAllowedId(string id)
    {
        // ASCII only: char.IsLetterOrDigit would also accept letters that are awkward in addresses
        static bool IsAsciiLetterOrDigit(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9');

        if (!IsAsciiLetterOrDigit(id[0]))
        {
            return false;
        }

        return id.All(c => IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
    }

    /// <summary>
    ///     Checks and normalizes a review title.
    /// </summary>
    /// <param name="title">The candidate title.</param>
    /// <returns>The title without leading and trailing white space.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ReviewTitleInvalid"/> when the title is empty or too long.</exception>
    private static string ValidateTitle(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainRuleException(DomainRule.ReviewTitleInvalid, "The review title is required.");
        }

        if (trimmed.Length > ReviewLimits.MaxReviewTitleLength)
        {
            throw new DomainRuleException(
                DomainRule.ReviewTitleInvalid,
                $"The review title must not be longer than {ReviewLimits.MaxReviewTitleLength} characters.");
        }

        return trimmed;
    }

    /// <summary>
    ///     Checks the file set against the count, path, size and uniqueness rules.
    /// </summary>
    /// <param name="files">The files offered for the review.</param>
    /// <exception cref="DomainRuleException">Thrown when a file rule is violated.</exception>
    private static void ValidateFiles(IReadOnlyList<UploadedFile> files)
    {
        if (files.Count == 0)
        {
            throw new DomainRuleException(DomainRule.NoFiles, "A review needs at least one file.");
        }

        if (files.Count > ReviewLimits.MaxFileCount)
        {
            throw new DomainRuleException(
                DomainRule.TooManyFiles,
                $"A review can have at most {ReviewLimits.MaxFileCount} files.");
        }

        // Paths are compared ordinally: file systems and ReviewMark treat paths as exact strings
        var paths = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.Path) || file.Path.Length > ReviewLimits.MaxPathLength)
            {
                throw new DomainRuleException(
                    DomainRule.PathInvalid,
                    $"A file path must be 1 to {ReviewLimits.MaxPathLength} characters.");
            }

            if (!paths.Add(file.Path))
            {
                throw new DomainRuleException(DomainRule.DuplicatePath, $"The file path '{file.Path}' appears more than once.");
            }

            if (file.Content.Length > ReviewLimits.MaxFileSizeBytes)
            {
                throw new DomainRuleException(
                    DomainRule.FileTooLarge,
                    $"The file '{file.Path}' is larger than the limit of {ReviewLimits.MaxFileSizeBytes / 1024 / 1024} MiB.");
            }

            total += file.Content.Length;
        }

        if (total > ReviewLimits.MaxTotalSizeBytes)
        {
            throw new DomainRuleException(
                DomainRule.TotalSizeExceeded,
                $"The files together are larger than the limit of {ReviewLimits.MaxTotalSizeBytes / 1024 / 1024} MiB.");
        }
    }
}
