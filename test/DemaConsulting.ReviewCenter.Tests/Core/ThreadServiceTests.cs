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

using System.Text;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="ThreadService"/> (and the line counting of <see cref="FileText"/> through it)
///     using an in-memory store.
/// </summary>
public class ThreadServiceTests
{
    /// <summary>
    ///     The instant reported by the service's clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Creates a store with a project and a review of a three-line text file <c>a.txt</c>, a binary file
    ///     <c>bin.dat</c> and an empty file <c>empty.txt</c>, and a thread service over it.
    /// </summary>
    /// <returns>The store, the service and the id of the project; the review is <c>rev-1</c>.</returns>
    private static async Task<(FakeReviewStore Store, ThreadService Service, int ProjectId)> CreateAsync()
    {
        var store = new FakeReviewStore();
        var project = await new ProjectService(store, new FixedTimeProvider(_now))
            .CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await new ReviewService(store, new FixedTimeProvider(_now)).CreateDraftAsync(
            project.Id,
            "rev-1",
            "First",
            [
                new("a.txt", Encoding.UTF8.GetBytes("one\ntwo\nthree\n")),
                new("bin.dat", TestContent.C),
                new("empty.txt", [])
            ],
            TestContext.Current.CancellationToken);
        return (store, new ThreadService(store, new FixedTimeProvider(_now)), project.Id);
    }

    /// <summary>
    ///     Test that a whole-file thread is stored open, with the clock time and its first comment.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_WholeFile_StoresOpenThreadWithFirstComment()
    {
        // Arrange: a review with files
        var (store, service, projectId) = await CreateAsync();

        // Act: start a whole-file thread
        var thread = await service.StartThreadAsync(
            projectId, "rev-1", "a.txt", null, null, "Ann", "Looks odd", TestContext.Current.CancellationToken);

        // Assert: open, whole file, one comment, clock time, stored
        Assert.Equal("a.txt", thread.FilePath);
        Assert.Null(thread.StartLine);
        Assert.Null(thread.EndLine);
        Assert.Equal(ReviewThreadState.Open, thread.State);
        Assert.Equal(_now, thread.Created);
        var comment = Assert.Single(thread.Comments);
        Assert.Equal("Ann", comment.Author);
        Assert.Equal("Looks odd", comment.Text);
        Assert.Equal(_now, comment.Created);
        Assert.Single(store.Threads);
    }

    /// <summary>
    ///     Test that a line range, including a single line, is stored as given.
    /// </summary>
    [Theory]
    [InlineData(2, 3)]
    [InlineData(2, 2)]
    public async Task ThreadService_StartThreadAsync_LineRange_StoresRange(int start, int end)
    {
        // Arrange: a review with a three-line file
        var (_, service, projectId) = await CreateAsync();

        // Act: start a thread on the range
        var thread = await service.StartThreadAsync(
            projectId, "rev-1", "a.txt", start, end, "Ann", "Text", TestContext.Current.CancellationToken);

        // Assert: the range is kept
        Assert.Equal(start, thread.StartLine);
        Assert.Equal(end, thread.EndLine);
    }

    /// <summary>
    ///     Test that the last line is accepted and the next one is refused, however the lines are separated.
    /// </summary>
    /// <param name="text">The file text.</param>
    /// <param name="lines">The number of lines the file has.</param>
    [Theory]
    [InlineData("a\nb", 2)]
    [InlineData("a\nb\n", 2)]
    [InlineData("a\r\nb\r\n", 2)]
    [InlineData("\n", 1)]
    public async Task ThreadService_StartThreadAsync_RangeLimits_AreInclusiveAndCountLinesBySeparator(string text, int lines)
    {
        // Arrange: a review with one file of the given text
        var store = new FakeReviewStore();
        var project = await new ProjectService(store, new FixedTimeProvider(_now))
            .CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await new ReviewService(store, new FixedTimeProvider(_now)).CreateDraftAsync(
            project.Id, "rev-1", "First", [new("f.txt", Encoding.UTF8.GetBytes(text))], TestContext.Current.CancellationToken);
        var service = new ThreadService(store, new FixedTimeProvider(_now));

        // Act: anchor to the last line, then one line beyond it
        var accepted = await service.StartThreadAsync(
            project.Id, "rev-1", "f.txt", lines, lines, "Ann", "Text", TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => service.StartThreadAsync(
            project.Id, "rev-1", "f.txt", lines, lines + 1, "Ann", "Text", TestContext.Current.CancellationToken));

        // Assert: the last line is inclusive, the next is refused
        Assert.Equal(lines, accepted.EndLine);
        Assert.Equal(DomainRule.ThreadAnchorInvalid, exception.Rule);
        Assert.Single(store.Threads);
    }

    /// <summary>
    ///     Test that inconsistent or out-of-range line numbers are refused and nothing is stored.
    /// </summary>
    /// <param name="start">The first line.</param>
    /// <param name="end">The last line.</param>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(1, null)]
    [InlineData(null, 1)]
    [InlineData(1, 999)]
    public async Task ThreadService_StartThreadAsync_InvalidRange_ThrowsThreadAnchorInvalid(int? start, int? end)
    {
        // Arrange: a review with a three-line file
        var (store, service, projectId) = await CreateAsync();

        // Act: start a thread with the invalid range
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => service.StartThreadAsync(
            projectId, "rev-1", "a.txt", start, end, "Ann", "Text", TestContext.Current.CancellationToken));

        // Assert: refused for the anchor and nothing stored
        Assert.Equal(DomainRule.ThreadAnchorInvalid, exception.Rule);
        Assert.Empty(store.Threads);
    }

    /// <summary>
    ///     Test that a line range cannot be used on a binary or an empty file.
    /// </summary>
    /// <param name="path">The file path.</param>
    [Theory]
    [InlineData("bin.dat")]
    [InlineData("empty.txt")]
    public async Task ThreadService_StartThreadAsync_RangeOnBinaryOrEmptyFile_ThrowsThreadAnchorInvalid(string path)
    {
        // Arrange: a review with a binary and an empty file
        var (store, service, projectId) = await CreateAsync();

        // Act: start a thread on line 1 of the file
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => service.StartThreadAsync(
            projectId, "rev-1", path, 1, 1, "Ann", "Text", TestContext.Current.CancellationToken));

        // Assert: refused for the anchor and nothing stored
        Assert.Equal(DomainRule.ThreadAnchorInvalid, exception.Rule);
        Assert.Empty(store.Threads);
    }

    /// <summary>
    ///     Test that a whole-file thread is allowed on a binary file and on an empty file.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_WholeFileOnBinaryFile_IsAllowed()
    {
        // Arrange: a review with a binary and an empty file
        var (_, service, projectId) = await CreateAsync();

        // Act: start a whole-file thread on each
        var binary = await service.StartThreadAsync(
            projectId, "rev-1", "bin.dat", null, null, "Ann", "Text", TestContext.Current.CancellationToken);
        var empty = await service.StartThreadAsync(
            projectId, "rev-1", "empty.txt", null, null, "Ann", "Text", TestContext.Current.CancellationToken);

        // Assert: both are stored
        Assert.Equal("bin.dat", binary.FilePath);
        Assert.Equal("empty.txt", empty.FilePath);
    }

    /// <summary>
    ///     Test that a file or review that does not exist cannot be commented on.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_UnknownFileOrReview_ThrowsThreadAnchorInvalid()
    {
        // Arrange: a review with files
        var (store, service, projectId) = await CreateAsync();

        // Act: start threads on an unknown file, with a wrong case, and on an unknown review
        var unknownFile = await Assert.ThrowsAsync<DomainRuleException>(() => service.StartThreadAsync(
            projectId, "rev-1", "nope.txt", null, null, "Ann", "Text", TestContext.Current.CancellationToken));
        var otherCase = await Assert.ThrowsAsync<DomainRuleException>(() => service.StartThreadAsync(
            projectId, "rev-1", "A.TXT", null, null, "Ann", "Text", TestContext.Current.CancellationToken));
        var unknownReview = await Assert.ThrowsAsync<DomainRuleException>(() => service.StartThreadAsync(
            projectId, "rev-9", "a.txt", null, null, "Ann", "Text", TestContext.Current.CancellationToken));

        // Assert: all refused for the anchor and nothing stored
        Assert.Equal(DomainRule.ThreadAnchorInvalid, unknownFile.Rule);
        Assert.Equal(DomainRule.ThreadAnchorInvalid, otherCase.Rule);
        Assert.Equal(DomainRule.ThreadAnchorInvalid, unknownReview.Rule);
        Assert.Empty(store.Threads);
    }

    /// <summary>
    ///     Test that a blank or too long author is refused and the longest allowed one is accepted.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_BlankOrLongAuthor_ThrowsCommentAuthorInvalid()
    {
        // Arrange: a review with files
        var (store, service, projectId) = await CreateAsync();

        // Act: start threads with a missing, blank and too long author, and one with the longest allowed
        var missing = await Assert.ThrowsAsync<DomainRuleException>(() => service.StartThreadAsync(
            projectId, "rev-1", "a.txt", null, null, null, "Text", TestContext.Current.CancellationToken));
        var blank = await Assert.ThrowsAsync<DomainRuleException>(() => service.StartThreadAsync(
            projectId, "rev-1", "a.txt", null, null, "   ", "Text", TestContext.Current.CancellationToken));
        var tooLong = await Assert.ThrowsAsync<DomainRuleException>(() => service.StartThreadAsync(
            projectId, "rev-1", "a.txt", null, null, new string('n', ReviewLimits.MaxAuthorLength + 1), "Text", TestContext.Current.CancellationToken));
        var longest = await service.StartThreadAsync(
            projectId, "rev-1", "a.txt", null, null, new string('n', ReviewLimits.MaxAuthorLength), "Text", TestContext.Current.CancellationToken);

        // Assert: only the longest allowed author is stored
        Assert.Equal(DomainRule.CommentAuthorInvalid, missing.Rule);
        Assert.Equal(DomainRule.CommentAuthorInvalid, blank.Rule);
        Assert.Equal(DomainRule.CommentAuthorInvalid, tooLong.Rule);
        Assert.Equal(ReviewLimits.MaxAuthorLength, longest.Comments.Single().Author.Length);
        Assert.Single(store.Threads);
    }

    /// <summary>
    ///     Test that a blank or too long text is refused and the longest allowed one is accepted.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_BlankOrLongText_ThrowsCommentTextInvalid()
    {
        // Arrange: a review with files
        var (store, service, projectId) = await CreateAsync();

        // Act: start threads with a missing, blank and too long text, and one with the longest allowed
        var missing = await Assert.ThrowsAsync<DomainRuleException>(() => service.StartThreadAsync(
            projectId, "rev-1", "a.txt", null, null, "Ann", null, TestContext.Current.CancellationToken));
        var blank = await Assert.ThrowsAsync<DomainRuleException>(() => service.StartThreadAsync(
            projectId, "rev-1", "a.txt", null, null, "Ann", " \n ", TestContext.Current.CancellationToken));
        var tooLong = await Assert.ThrowsAsync<DomainRuleException>(() => service.StartThreadAsync(
            projectId, "rev-1", "a.txt", null, null, "Ann", new string('t', ReviewLimits.MaxCommentLength + 1), TestContext.Current.CancellationToken));
        var longest = await service.StartThreadAsync(
            projectId, "rev-1", "a.txt", null, null, "Ann", new string('t', ReviewLimits.MaxCommentLength), TestContext.Current.CancellationToken);

        // Assert: only the longest allowed text is stored
        Assert.Equal(DomainRule.CommentTextInvalid, missing.Rule);
        Assert.Equal(DomainRule.CommentTextInvalid, blank.Rule);
        Assert.Equal(DomainRule.CommentTextInvalid, tooLong.Rule);
        Assert.Equal(ReviewLimits.MaxCommentLength, longest.Comments.Single().Text.Length);
        Assert.Single(store.Threads);
    }

    /// <summary>
    ///     Test that author and text are stored without surrounding white space, keeping inner line breaks.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_PaddedAuthorAndText_AreTrimmed()
    {
        // Arrange: a review with files
        var (_, service, projectId) = await CreateAsync();

        // Act: start a thread with padded author and a multi-line text
        var thread = await service.StartThreadAsync(
            projectId, "rev-1", "a.txt", null, null, "  Ann  ", "\n  line one\nline two \n", TestContext.Current.CancellationToken);

        // Assert: trimmed at the ends only
        var comment = Assert.Single(thread.Comments);
        Assert.Equal("Ann", comment.Author);
        Assert.Equal("line one\nline two", comment.Text);
    }

    /// <summary>
    ///     Test that a reply is appended in posting order, leaves earlier comments and the state alone, and is
    ///     allowed on a resolved thread.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReplyAsync_ExistingThread_AppendsInOrderAndKeepsEarlierComments()
    {
        // Arrange: a resolved thread with its first comment
        var (store, service, projectId) = await CreateAsync();
        var thread = await service.StartThreadAsync(
            projectId, "rev-1", "a.txt", null, null, "Ann", "First", TestContext.Current.CancellationToken);
        await service.ResolveAsync(projectId, "rev-1", thread.Id, TestContext.Current.CancellationToken);

        // Act: reply twice
        var first = await service.ReplyAsync(projectId, "rev-1", thread.Id, " Bob ", " Second ", TestContext.Current.CancellationToken);
        await service.ReplyAsync(projectId, "rev-1", thread.Id, "Cy", "Third", TestContext.Current.CancellationToken);

        // Assert: the reply is trimmed and timed, the comments are in order, the state is unchanged
        Assert.Equal("Bob", first.Author);
        Assert.Equal("Second", first.Text);
        Assert.Equal(_now, first.Created);
        var stored = Assert.Single(store.Threads);
        Assert.Equal(["First", "Second", "Third"], stored.Comments.Select(c => c.Text));
        Assert.Equal(ReviewThreadState.Resolved, stored.State);
    }

    /// <summary>
    ///     Test that an invalid reply is refused and adds nothing.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReplyAsync_InvalidAuthorOrText_ThrowsAndStoresNothing()
    {
        // Arrange: a thread with its first comment
        var (store, service, projectId) = await CreateAsync();
        var thread = await service.StartThreadAsync(
            projectId, "rev-1", "a.txt", null, null, "Ann", "First", TestContext.Current.CancellationToken);

        // Act: reply with a blank author and with a blank text
        var author = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.ReplyAsync(projectId, "rev-1", thread.Id, " ", "Text", TestContext.Current.CancellationToken));
        var text = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.ReplyAsync(projectId, "rev-1", thread.Id, "Bob", " ", TestContext.Current.CancellationToken));

        // Assert: the rules are named and the thread still has one comment
        Assert.Equal(DomainRule.CommentAuthorInvalid, author.Rule);
        Assert.Equal(DomainRule.CommentTextInvalid, text.Rule);
        Assert.Single(Assert.Single(store.Threads).Comments);
    }

    /// <summary>
    ///     Test that a reply, resolve or reopen of a thread that is not in the review is refused.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReplyAsync_UnknownThreadOrOtherReview_ThrowsThreadNotFound()
    {
        // Arrange: a thread in review rev-1 and a second review
        var (store, service, projectId) = await CreateAsync();
        await new ReviewService(store, new FixedTimeProvider(_now)).CreateDraftAsync(
            projectId, "rev-2", "Second", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        var thread = await service.StartThreadAsync(
            projectId, "rev-1", "a.txt", null, null, "Ann", "First", TestContext.Current.CancellationToken);

        // Act: reply to an unknown thread, and to the thread through the other review
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.ReplyAsync(projectId, "rev-1", 99, "Bob", "Text", TestContext.Current.CancellationToken));
        var otherReview = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.ReplyAsync(projectId, "rev-2", thread.Id, "Bob", "Text", TestContext.Current.CancellationToken));

        // Assert: both refused and the thread is unchanged
        Assert.Equal(DomainRule.ThreadNotFound, unknown.Rule);
        Assert.Equal(DomainRule.ThreadNotFound, otherReview.Rule);
        Assert.Single(Assert.Single(store.Threads).Comments);
    }

    /// <summary>
    ///     Test that resolving and reopening change the state only, and that repeating either is harmless.
    /// </summary>
    [Fact]
    public async Task ThreadService_ResolveAsync_ReopenAsync_ChangeStateKeepCommentsAndAreIdempotent()
    {
        // Arrange: a thread with a reply
        var (store, service, projectId) = await CreateAsync();
        var thread = await service.StartThreadAsync(
            projectId, "rev-1", "a.txt", null, null, "Ann", "First", TestContext.Current.CancellationToken);
        await service.ReplyAsync(projectId, "rev-1", thread.Id, "Bob", "Second", TestContext.Current.CancellationToken);

        // Act: resolve twice, then reopen twice
        await service.ResolveAsync(projectId, "rev-1", thread.Id, TestContext.Current.CancellationToken);
        await service.ResolveAsync(projectId, "rev-1", thread.Id, TestContext.Current.CancellationToken);
        var resolved = Assert.Single(store.Threads).State;
        await service.ReopenAsync(projectId, "rev-1", thread.Id, TestContext.Current.CancellationToken);
        await service.ReopenAsync(projectId, "rev-1", thread.Id, TestContext.Current.CancellationToken);
        var reopened = Assert.Single(store.Threads);

        // Assert: the states changed and both comments are still there
        Assert.Equal(ReviewThreadState.Resolved, resolved);
        Assert.Equal(ReviewThreadState.Open, reopened.State);
        Assert.Equal(["First", "Second"], reopened.Comments.Select(c => c.Text));
    }

    /// <summary>
    ///     Test that resolving or reopening an unknown thread is refused.
    /// </summary>
    [Fact]
    public async Task ThreadService_ResolveAsync_UnknownThread_ThrowsThreadNotFound()
    {
        // Arrange: a review without threads
        var (_, service, projectId) = await CreateAsync();

        // Act: resolve and reopen a thread that does not exist
        var resolve = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.ResolveAsync(projectId, "rev-1", 99, TestContext.Current.CancellationToken));
        var reopen = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.ReopenAsync(projectId, "rev-1", 99, TestContext.Current.CancellationToken));

        // Assert: both name the missing thread
        Assert.Equal(DomainRule.ThreadNotFound, resolve.Rule);
        Assert.Equal(DomainRule.ThreadNotFound, reopen.Rule);
    }

    /// <summary>
    ///     Test that all threads of a review are listed in creation order with their comments.
    /// </summary>
    [Fact]
    public async Task ThreadService_ListAsync_SeveralThreads_ReturnsAllInOrderWithComments()
    {
        // Arrange: threads on two files, one with a reply
        var (_, service, projectId) = await CreateAsync();
        var first = await service.StartThreadAsync(
            projectId, "rev-1", "a.txt", 1, 1, "Ann", "One", TestContext.Current.CancellationToken);
        await service.StartThreadAsync(
            projectId, "rev-1", "bin.dat", null, null, "Bob", "Two", TestContext.Current.CancellationToken);
        await service.ReplyAsync(projectId, "rev-1", first.Id, "Cy", "Reply", TestContext.Current.CancellationToken);

        // Act: list the threads
        var threads = await service.ListAsync(projectId, "rev-1", TestContext.Current.CancellationToken);

        // Assert: both threads in creation order, the first with both comments
        Assert.Equal(["a.txt", "bin.dat"], threads.Select(t => t.FilePath));
        Assert.Equal(["One", "Reply"], threads[0].Comments.Select(c => c.Text));
        Assert.Equal(["Two"], threads[1].Comments.Select(c => c.Text));
    }

    /// <summary>
    ///     Test that a review without threads, and a review that does not exist, list nothing.
    /// </summary>
    [Fact]
    public async Task ThreadService_ListAsync_NoThreadsOrUnknownReview_ReturnsEmpty()
    {
        // Arrange: a review without threads
        var (_, service, projectId) = await CreateAsync();

        // Act: list the threads of it and of an unknown review
        var none = await service.ListAsync(projectId, "rev-1", TestContext.Current.CancellationToken);
        var unknown = await service.ListAsync(projectId, "rev-9", TestContext.Current.CancellationToken);

        // Assert: both are empty
        Assert.Empty(none);
        Assert.Empty(unknown);
    }

    /// <summary>
    ///     Test that the constructor refuses a missing store or clock.
    /// </summary>
    [Fact]
    public void ThreadService_Constructor_NullArguments_Throws()
    {
        // Arrange, Act, Assert: each missing dependency is refused
        Assert.Throws<ArgumentNullException>(() => new ThreadService(null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new ThreadService(new FakeReviewStore(), null!));
    }
}
