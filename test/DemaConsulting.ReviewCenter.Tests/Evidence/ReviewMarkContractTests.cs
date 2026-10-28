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

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static DemaConsulting.ReviewCenter.Tests.Api.ApiTestSupport;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Contract tests between the evidence index and ReviewMark: the document has the schema ReviewMark reads, it
///     is the committed golden file for a fixed project, and its fingerprints are the ones ReviewMark computes for the
///     same files.
/// </summary>
/// <remarks>
///     ReviewMark 1.4.0 reads <c>{"reviews":[{id, fingerprint, date, result, file}]}</c>, keeps one entry per
///     (id, fingerprint), and classifies a review as current, failed, stale or missing; the readers in this class
///     mirror that behavior. The fixture is the same for every test: a fixed clock, fixed ids and fixed files, so the
///     golden file never varies.
/// </remarks>
public class ReviewMarkContractTests
{
    /// <summary>
    ///     The fingerprint ReviewMark 1.4.0 computes for the files <c>hello\n</c> and <c>world\n</c>.
    /// </summary>
    private const string FingerprintAB = "9e7cc9e1540e8b9cc91eb6a7da9ec924144b2eaaae26097c6427dcf547711add";

    /// <summary>
    ///     The reviews of the fixture: identifier, review-set id, file contents and whether the review is signed.
    /// </summary>
    private static readonly (string Id, string? SetId, byte[][] Files, bool Signed)[] Reviews =
    [
        ("R-1", "Core-Logic", [TestContent.A, TestContent.B], true),
        ("R-2", "Core-Logic", [TestContent.A], true),
        ("R-3", "Api-Layer", [TestContent.A, TestContent.B, TestContent.C], true),
        ("R-4", "Draft-Only", [TestContent.A], false),
        ("R-5", null, [TestContent.B], true)
    ];

    /// <summary>
    ///     The index of the fixed project, generated once for all tests of the class: starting the application and
    ///     signing the reviews with the real password hasher is slow, and the result is the same every time.
    /// </summary>
    private static readonly Lazy<Task<string>> SharedIndex = new(() => GenerateIndexAsync(CancellationToken.None));

    /// <summary>
    ///     Test that the index of the fixed project is the committed golden file, byte for byte after the line endings
    ///     are normalized.
    /// </summary>
    [Fact(Timeout = 180000)]
    public async Task ReviewMarkContract_GoldenIndex_MatchesCommittedFile()
    {
        // Arrange: the index of the fixed project, as indented JSON, and the golden file
        var text = await SharedIndex.Value.WaitAsync(TestContext.Current.CancellationToken);
        var generated = Normalize(JsonSerializer.Serialize(JsonDocument.Parse(text).RootElement, new JsonSerializerOptions { WriteIndented = true }));
        var path = Path.Combine(FindRepositoryRoot(), "test", "DemaConsulting.ReviewCenter.Tests", "Evidence", "TestData", "evidence-index.golden.json");

        // Act: read the golden file
        var golden = Normalize(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));

        // Assert: the same document
        Assert.Equal(golden, generated);
    }

    /// <summary>
    ///     Test that the index has the schema ReviewMark reads and that ReviewMark's reader loads every entry without
    ///     overwriting another.
    /// </summary>
    [Fact(Timeout = 180000)]
    public async Task ReviewMarkContract_Index_HasTheSchemaReviewMarkReads()
    {
        // Arrange: the index of the fixed project
        var text = await SharedIndex.Value.WaitAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;

        // Assert: an object with the one property "reviews", an array of objects with five non-blank string fields
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(["reviews"], root.EnumerateObject().Select(p => p.Name).ToArray());
        var reviews = root.GetProperty("reviews");
        Assert.Equal(JsonValueKind.Array, reviews.ValueKind);
        Assert.Equal(3, reviews.GetArrayLength());
        foreach (var review in reviews.EnumerateArray())
        {
            Assert.Equal(["id", "fingerprint", "date", "result", "file"], review.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.All(review.EnumerateObject(), p =>
            {
                Assert.Equal(JsonValueKind.String, p.Value.ValueKind);
                Assert.False(string.IsNullOrWhiteSpace(p.Value.GetString()));
            });
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", review.GetProperty("date").GetString());
            Assert.Equal("pass", review.GetProperty("result").GetString());
            Assert.True(Uri.TryCreate(review.GetProperty("file").GetString(), UriKind.Absolute, out _));
        }

        // Assert: ReviewMark's reader keeps every entry (no later entry overwrites an earlier one)
        var index = ReadLikeReviewMark(text, out var overwritten);
        Assert.Equal(0, overwritten);
        Assert.Equal(3, index.Values.Sum(byFingerprint => byFingerprint.Count));
    }

    /// <summary>
    ///     Test that the fingerprints in the index are the ones ReviewMark's algorithm gives for the same files, and
    ///     that the known vectors agree with it too.
    /// </summary>
    [Fact(Timeout = 180000)]
    public async Task ReviewMarkContract_IndexFingerprint_EqualsReviewMarkAlgorithm()
    {
        // Arrange: the index of the fixed project, as ReviewMark reads it
        var index = ReadLikeReviewMark(await SharedIndex.Value.WaitAsync(TestContext.Current.CancellationToken), out _);

        // Assert: every listed review has the fingerprint of the independent implementation
        foreach (var review in Reviews.Where(r => r.SetId is not null && r.Signed))
        {
            Assert.True(
                index[review.SetId!].ContainsKey(ReviewMarkFingerprint(review.Files)),
                $"The index has no entry of '{review.SetId}' with the ReviewMark fingerprint of the files of {review.Id}.");
        }

        // Assert: the independent implementation reproduces the vectors verified against ReviewMark 1.4.0
        Assert.Equal(TestContent.FingerprintA, ReviewMarkFingerprint([TestContent.A]));
        Assert.Equal(TestContent.FingerprintABC, ReviewMarkFingerprint([TestContent.A, TestContent.B, TestContent.C]));
        Assert.Equal(TestContent.FingerprintAA, ReviewMarkFingerprint([TestContent.A, TestContent.A]));
        Assert.Equal(FingerprintAB, ReviewMarkFingerprint([TestContent.A, TestContent.B]));
        Assert.Equal(FingerprintAB, Fingerprint.Compute([TestContent.B, TestContent.A]));
    }

    /// <summary>
    ///     Test that ReviewMark's rules classify the index as expected: current for the fingerprint of a listed
    ///     review, stale for another fingerprint of a listed id (naming the newest entry by its date), missing for an
    ///     id that is not listed.
    /// </summary>
    [Fact(Timeout = 180000)]
    public async Task ReviewMarkContract_Index_FollowsReviewMarkSemantics()
    {
        // Arrange: the index of the fixed project, as ReviewMark reads it
        var index = ReadLikeReviewMark(await SharedIndex.Value.WaitAsync(TestContext.Current.CancellationToken), out _);

        // Act and Assert: current, stale (with the newest date), missing
        Assert.Equal("current", Classify(index, "Core-Logic", FingerprintAB));
        Assert.Equal("current", Classify(index, "Api-Layer", TestContent.FingerprintABC));
        Assert.Equal("stale 2026-02-15", Classify(index, "Core-Logic", TestContent.FingerprintAA));
        Assert.Equal("missing", Classify(index, "Draft-Only", TestContent.FingerprintA));
        Assert.Equal("missing", Classify(index, "Unknown", TestContent.FingerprintA));
    }

    /// <summary>
    ///     Computes a fingerprint as ReviewMark does, independently of the production code: the SHA-256 of every
    ///     file as lowercase hexadecimal text, sorted ordinally, concatenated, hashed again as UTF-8 text.
    /// </summary>
    /// <param name="files">The contents of the files.</param>
    /// <returns>The fingerprint as lowercase hexadecimal text.</returns>
    private static string ReviewMarkFingerprint(IEnumerable<byte[]> files)
    {
        var hashes = files
            .Select(file => Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant())
            .OrderBy(hash => hash, StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(hashes)))).ToLowerInvariant();
    }

    /// <summary>
    ///     Reads an index like ReviewMark does: case-insensitive property names, an entry with a blank id or
    ///     fingerprint skipped, entries keyed by id and fingerprint with a later duplicate overwriting an earlier one.
    /// </summary>
    /// <param name="json">The index text.</param>
    /// <param name="overwritten">The number of entries that overwrote an earlier one.</param>
    /// <returns>The entries by id and fingerprint, as (date, result).</returns>
    private static Dictionary<string, Dictionary<string, (string Date, string Result)>> ReadLikeReviewMark(
        string json,
        out int overwritten)
    {
        overwritten = 0;
        var index = new Dictionary<string, Dictionary<string, (string, string)>>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(json);
        var reviews = document.RootElement.EnumerateObject()
            .Single(p => string.Equals(p.Name, "reviews", StringComparison.OrdinalIgnoreCase)).Value;
        foreach (var review in reviews.EnumerateArray())
        {
            var fields = review.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            var id = fields.GetValueOrDefault("id", string.Empty);
            var fingerprint = fields.GetValueOrDefault("fingerprint", string.Empty);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(fingerprint))
            {
                continue;
            }

            if (!index.TryGetValue(id, out var byFingerprint))
            {
                index[id] = byFingerprint = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            }

            overwritten += byFingerprint.ContainsKey(fingerprint) ? 1 : 0;
            byFingerprint[fingerprint] = (fields.GetValueOrDefault("date", string.Empty), fields.GetValueOrDefault("result", string.Empty));
        }

        return index.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>
    ///     Classifies a review the way ReviewMark's report does.
    /// </summary>
    /// <param name="index">The entries by id and fingerprint.</param>
    /// <param name="id">The review-set id.</param>
    /// <param name="fingerprint">The current fingerprint of the review-set.</param>
    /// <returns><c>current</c>, <c>failed</c>, <c>stale</c> followed by the newest date, or <c>missing</c>.</returns>
    private static string Classify(
        Dictionary<string, Dictionary<string, (string Date, string Result)>> index,
        string id,
        string fingerprint)
    {
        if (!index.TryGetValue(id, out var byFingerprint))
        {
            return "missing";
        }

        if (byFingerprint.TryGetValue(fingerprint, out var match))
        {
            return string.Equals(match.Result, "pass", StringComparison.OrdinalIgnoreCase) ? "current" : "failed";
        }

        // The newest entry is picked by an ordinal comparison of the date text
        return "stale " + byFingerprint.Values.Select(v => v.Date).Max(StringComparer.Ordinal);
    }

    /// <summary>
    ///     Starts the application with a fixed clock, creates the fixed project and fetches its evidence index the way
    ///     ReviewMark does: with HTTP Basic credentials and a personal access token as the password.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token of the test.</param>
    /// <returns>The text of the index.</returns>
    private static async Task<string> GenerateIndexAsync(CancellationToken cancellationToken)
    {
        var clock = new MovableTimeProvider(new DateTimeOffset(2026, 2, 14, 10, 0, 0, TimeSpan.Zero));
        await using var app = await StartAsync(configureServices: services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)));
        var token = await CreateTokenAsync(app);
        var users = app.Services.GetRequiredService<UserService>();
        var annId = (await users.GetAsync("ann", cancellationToken))!.Id;
        var project = await app.Services.GetRequiredService<ProjectService>().CreateAsync("Apollo", null, cancellationToken);
        var store = app.Services.GetRequiredService<IReviewStore>();
        await store.AddMembershipAsync(annId, project.Id, cancellationToken);

        // Each review is completed on its own day, starting with the day of the clock
        var checklists = app.Services.GetRequiredService<ChecklistService>();
        var signing = app.Services.GetRequiredService<SigningService>();
        foreach (var review in Reviews)
        {
            await EvidenceTestSupport.AddReviewAsync(store, clock, project.Id, review.Id, review.SetId, review.Files);
            await EvidenceTestSupport.AnswerAllPassAsync(checklists, project.Id, review.Id);
            var slotId = await EvidenceTestSupport.AddSlotAsync(signing, project.Id, review.Id, annId);
            if (review.Signed)
            {
                await EvidenceTestSupport.SignAsync(signing, project.Id, review.Id, slotId, EvidenceTestSupport.Ann, TestBrowser.Password);
            }

            clock.Advance(TimeSpan.FromDays(1));
        }

        using var client = ClientFor(app, null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("reviewmark:" + token.Value)));
        var response = await client.GetAsync($"/api/projects/{project.Id}/evidence/index.json", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>
    ///     Makes the line endings LF and ends the text with one newline, so a checkout setting cannot make a
    ///     difference.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The normalized text.</returns>
    private static string Normalize(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";
    }

    /// <summary>
    ///     Finds the root of the repository: the folder above the test binaries that has the solution file.
    /// </summary>
    /// <returns>The folder.</returns>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DemaConsulting.ReviewCenter.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test binaries.");
    }
}
