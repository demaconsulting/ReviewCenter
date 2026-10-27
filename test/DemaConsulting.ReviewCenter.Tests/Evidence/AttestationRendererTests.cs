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

using DemaConsulting.ReviewCenter.Evidence;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Unit tests for <see cref="AttestationRenderer"/>: what the unsigned attestation says, from the document that
///     the service assembles.
/// </summary>
public class AttestationRendererTests
{
    /// <summary>
    ///     The record digest of the sample document: 64 hexadecimal characters.
    /// </summary>
    private const string Digest = "aaaabbbbccccddddeeeeffff0000111122223333444455556666777788889999";

    /// <summary>
    ///     The audit chain head of the sample document: 64 hexadecimal characters.
    /// </summary>
    private const string Head = "1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd";

    /// <summary>
    ///     The time of generation of the sample document.
    /// </summary>
    private static readonly DateTimeOffset _generated = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Describes a self-signed key.
    /// </summary>
    /// <param name="selfSigned">Whether the key is self-signed.</param>
    /// <returns>The description.</returns>
    internal static AttestationKeyInfo KeyInfo(bool selfSigned = true)
    {
        return new AttestationKeyInfo(
            selfSigned ? "SelfSigned" : "CertificateStore",
            selfSigned ? "CN=Self signed test key" : "CN=Org Signer, O=Org",
            selfSigned ? "CN=Self signed test key" : "CN=Org CA",
            "0123456789ABCDEF0123456789ABCDEF01234567",
            "0A0B0C",
            "RSA-3072",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero),
            selfSigned,
            true,
            !selfSigned);
    }

    /// <summary>
    ///     Creates a complete sample document.
    /// </summary>
    /// <param name="key">The signing key description.</param>
    /// <returns>The document.</returns>
    internal static AttestationDocument Sample(AttestationKeyInfo? key = null)
    {
        return new AttestationDocument(
            7,
            "Apollo",
            "rev-1",
            "First review",
            "apollo-repo",
            "b".PadRight(40, 'b'),
            "a".PadRight(40, 'a'),
            "set-core",
            "fingerprint-1234",
            Digest,
            "ReviewCenter record v4",
            [
                new AttestationFile("src/a.txt", "Modified", "0f".PadRight(64, '0'), 120),
                new AttestationFile("src/b.txt", "Added", "1f".PadRight(64, '1'), 33)
            ],
            "1.0",
            [
                new AttestationSection(
                    "Correctness",
                    true,
                    null,
                    [
                        new AttestationCheck("C-1", "Does it compile?", "Pass", "Built on CI", "Ann"),
                        new AttestationCheck("C-2", "Are errors handled?", "Fail", null, "Ann"),
                        new AttestationCheck("C-3", "Is it thread safe?", "N/A", "Single threaded", "Bob")
                    ]),
                new AttestationSection("Security", false, "No external input", [])
            ],
            2,
            [new AttestationThread("src/a.txt, lines 3-5", "Bob: please rename this", 3)],
            [
                new AttestationSlot(
                    1,
                    "Ann (ann)",
                    "Bob (bob)",
                    new AttestationSignature("Bob (bob)", "Alternate", new DateTimeOffset(2025, 3, 3, 4, 5, 6, TimeSpan.Zero), "ReviewCenter record v4", Digest, "I reviewed it")),
                new AttestationSlot(2, "Cyd (cyd)", null, null)
            ],
            Head,
            42,
            true,
            key ?? KeyInfo(),
            "1.2.3",
            _generated,
            "Ann (ann)");
    }

    /// <summary>
    ///     Renders a document and returns the text of the pages.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The inspector over the rendered PDF.</returns>
    private static PdfInspector Render(AttestationDocument document)
    {
        return new PdfInspector(new AttestationRenderer().Render(document));
    }

    /// <summary>
    ///     Test that the document names the project, the review and where the content came from.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_Document_ShowsReviewIdentityRevisionsAndFingerprint()
    {
        // Arrange: a sample document

        // Act: render
        var lines = Render(Sample()).TextLines;

        // Assert: project, review, repository, both revisions, set and fingerprint
        Assert.Contains("Apollo (#7)", lines);
        Assert.Contains("First review", lines);
        Assert.Contains("rev-1", lines);
        Assert.Contains("apollo-repo", lines);
        Assert.Contains("a".PadRight(40, 'a'), lines);
        Assert.Contains("b".PadRight(40, 'b'), lines);
        Assert.Contains("set-core", lines);
        Assert.Contains("fingerprint-1234", lines);
        Assert.Contains("2025-03-04 05:06:07 UTC by Ann (ann)", lines);
    }

    /// <summary>
    ///     Test that a review without a repository says that its files were uploaded and that a missing baseline
    ///     is explained.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_UploadedReview_SaysFilesWereUploaded()
    {
        // Arrange: a document without repository
        var document = Sample() with { RepositoryName = null, BaselineSha = null, HeadSha = null, ReviewSetId = null };

        // Act: render
        var lines = Render(document).TextLines;

        // Assert: the source line, and no revision rows
        Assert.Contains("Files uploaded to ReviewCenter", lines);
        Assert.DoesNotContain("Head revision", lines);
    }

    /// <summary>
    ///     Test that every file is listed with its status.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_Document_ListsFilesWithStatus()
    {
        // Arrange: a sample document

        // Act: render
        var lines = Render(Sample()).TextLines;

        // Assert: both files and their status, size and hash
        Assert.Contains("Files (2)", lines);
        Assert.Contains("src/a.txt", lines);
        Assert.Contains(lines, l => l.StartsWith("Modified, 120 bytes, SHA-256 0f", StringComparison.Ordinal));
        Assert.Contains("src/b.txt", lines);
        Assert.Contains(lines, l => l.StartsWith("Added, 33 bytes, SHA-256 1f", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that the checklist shows each answer and each comment, and the sections that do not apply.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_Document_ShowsChecklistAnswersAndComments()
    {
        // Arrange: a sample document with Pass, Fail and N/A answers

        // Act: render
        var lines = Render(Sample()).TextLines;

        // Assert: the labels carry the outcome, the comments are shown, the section that does not apply says why
        Assert.Contains("C-1 Pass", lines);
        Assert.Contains("C-2 Fail", lines);
        Assert.Contains("C-3 N/A", lines);
        Assert.Contains("Does it compile?", lines);
        Assert.Contains("Comment: Built on CI", lines);
        Assert.Contains("Comment: Single threaded", lines);
        Assert.Contains("Not applicable: No external input", lines);
        Assert.Contains("Answered by Bob", lines);
    }

    /// <summary>
    ///     Test that the thread summary gives the open and resolved counts and describes resolved threads.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_Document_SummarizesThreads()
    {
        // Arrange: a sample document with two open threads and one resolved

        // Act: render
        var lines = Render(Sample()).TextLines;
        var text = string.Join("\n", lines);

        // Assert: counts and the resolved thread
        Assert.Contains("Open\n2\n", text, StringComparison.Ordinal);
        Assert.Contains("Resolved\n1\n", text, StringComparison.Ordinal);
        Assert.Contains("src/a.txt, lines 3-5 (3 comments)", lines);
        Assert.Contains("Bob: please rename this", lines);
    }

    /// <summary>
    ///     Test that each reviewer slot shows its members, who signed, when, and the digest and version that the
    ///     signature bound to.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_Document_ShowsSlotsSignersTimestampsAndDigests()
    {
        // Arrange: a sample document with a signed slot and an unsigned one

        // Act: render
        var lines = Render(Sample()).TextLines;

        // Assert: members, signer with seat, time, meaning, digest with its version
        Assert.Contains("Slot 1", lines);
        Assert.Contains("Ann (ann)", lines);
        Assert.Contains("Bob (bob)", lines);
        Assert.Contains("Bob (bob) (Alternate)", lines);
        Assert.Contains("2025-03-03 04:05:06 UTC", lines);
        Assert.Contains("I reviewed it", lines);
        Assert.Contains(Digest, string.Concat(lines.Where(l => l.All(Uri.IsHexDigit))));
        Assert.Contains("(ReviewCenter record v4)", lines);
        Assert.Contains("Slot 2", lines);
        Assert.Contains("none", lines);
    }

    /// <summary>
    ///     Test that the 64-character record digest of a signed slot is shown on one line, not wrapped.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_Document_ShowsSlotDigestOnOneLine()
    {
        // Arrange: a sample document with a signed slot

        // Act: render
        var lines = Render(Sample()).TextLines;

        // Assert: the whole digest is a single line
        Assert.Contains(Digest, lines);
    }

    /// <summary>
    ///     Test that the audit chain head and the tool version are in the document, the version in every footer.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_Document_ShowsAuditHeadAndToolVersion()
    {
        // Arrange: a sample document

        // Act: render
        var pdf = Render(Sample());
        var lines = pdf.TextLines;

        // Assert: head hash, entry count, intact flag; the footer and the author name the version
        Assert.Contains("42", lines);
        Assert.Contains("yes, the hash chain is intact", lines);
        Assert.Contains(Head, string.Concat(lines.Where(l => l.All(Uri.IsHexDigit))));
        Assert.Equal(pdf.PageCount, lines.Count(l => l == "ReviewCenter 1.2.3"));
        Assert.Equal("ReviewCenter 1.2.3", pdf.Info("Author"));
    }

    /// <summary>
    ///     Test that the metadata carries the content fingerprint and the review identity.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_Document_SetsMetadataWithFingerprintAndReviewId()
    {
        // Arrange: a sample document

        // Act: render
        var pdf = Render(Sample());

        // Assert: title, subject and keywords identify the review
        Assert.Equal("Review attestation - First review (rev-1)", pdf.Info("Title"));
        Assert.Contains("rev-1", pdf.Info("Subject"), StringComparison.Ordinal);
        Assert.Equal("fingerprint:fingerprint-1234; review:rev-1; project:7", pdf.Info("Keywords"));
    }

    /// <summary>
    ///     Test that a self-signed key is announced in the document in capitals.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_SelfSignedKey_PrintsTheNotice()
    {
        // Arrange: a document signed with a self-signed key

        // Act: render
        var lines = Render(Sample(KeyInfo(selfSigned: true))).TextLines;

        // Assert: the notice is printed, the key facts and the flag
        Assert.Contains(lines, l => l.StartsWith("SELF-SIGNED KEY - NOT AN ORGANIZATION KEY", StringComparison.Ordinal));
        Assert.Contains("CN=Self signed test key", lines);
        Assert.Contains("0123456789ABCDEF0123456789ABCDEF01234567", lines);
        Assert.Contains("yes", lines);
    }

    /// <summary>
    ///     Test that an organization key is described by its subject, issuer and thumbprint, without the notice.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_OrganizationKey_PrintsSubjectIssuerAndNoNotice()
    {
        // Arrange: a document signed with a key from an authority
        var key = KeyInfo(selfSigned: false);

        // Act: render
        var lines = Render(Sample(key)).TextLines;

        // Assert: no notice; subject, issuer, thumbprint and the policy flag are there
        Assert.DoesNotContain(lines, l => l.Contains("SELF-SIGNED", StringComparison.Ordinal));
        Assert.Contains("CN=Org Signer, O=Org", lines);
        Assert.Contains("CN=Org CA", lines);
        Assert.Contains("0123456789ABCDEF0123456789ABCDEF01234567", lines);
        Assert.Contains("yes (Attestation:RequireTrustedKey)", lines);
    }

    /// <summary>
    ///     Test that the document says what the signature proves and what it does not.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_Document_ExplainsWhatTheSignatureProves()
    {
        // Arrange: a sample document

        // Act: render
        var lines = Render(Sample()).TextLines;

        // Assert: the heading and the statement, which separates the key from the reviewers' identity; the
        // statement is wrapped, so its first line is the start of it
        Assert.Contains("What this signature proves", lines);
        Assert.Contains(lines, l => l.Length > 30 && AttestationRenderer.ProvesStatement.StartsWith(l, StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("proves that this document has not changed", StringComparison.Ordinal));
        Assert.Contains("does not identify the reviewers", AttestationRenderer.ProvesStatement, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the same document gives the same bytes.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_SameDocument_ProducesIdenticalBytes()
    {
        // Arrange: a sample document

        // Act: render twice
        var first = new AttestationRenderer().Render(Sample());
        var second = new AttestationRenderer().Render(Sample());

        // Assert: identical
        Assert.Equal(first, second);
    }

    /// <summary>
    ///     Test that a document with many files runs over several pages and still has valid structure.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_ManyFiles_BreaksIntoSeveralPages()
    {
        // Arrange: 300 files
        var files = Enumerable.Range(1, 300)
            .Select(n => new AttestationFile($"src/file{n}.cs", "Added", "0".PadRight(64, 'f'), n))
            .ToList();
        var document = Sample() with { Files = files };

        // Act: render
        var pdf = Render(document);

        // Assert: several pages, the last file is present, the structure is consistent
        Assert.True(pdf.PageCount > 3);
        Assert.Contains("src/file300.cs", pdf.TextLines);
        Assert.True(pdf.CrossReferenceIsConsistent());
    }

    /// <summary>
    ///     Test that text that the standard fonts cannot show is replaced by question marks and does not break the file.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_UnicodeNames_AreReplacedByQuestionMarks()
    {
        // Arrange: a project and a reviewer with names outside Windows-1252
        var document = Sample() with { ProjectName = "\u963F\u6CE2\u7F57", GeneratedBy = "\u5F20\u4F1F (zhang)" };

        // Act: render
        var pdf = Render(document);

        // Assert: replaced by question marks, nothing else damaged; the metadata keeps the real text
        Assert.Contains("??? (#7)", pdf.TextLines);
        Assert.Contains("2025-03-04 05:06:07 UTC by ?? (zhang)", pdf.TextLines);
        Assert.Contains("\u963F\u6CE2\u7F57", pdf.Info("Subject"), StringComparison.Ordinal);
        Assert.True(pdf.CrossReferenceIsConsistent());
    }

    /// <summary>
    ///     Test that a review without checklist says so instead of printing an empty section.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_NoChecklist_SaysSo()
    {
        // Arrange: a document without sections
        var document = Sample() with { Sections = [], ChecklistVersion = null };

        // Act: render
        var lines = Render(document).TextLines;

        // Assert: the statement
        Assert.Contains("The review has no checklist.", lines);
    }

    /// <summary>
    ///     Test that a null document is refused.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_NullDocument_Throws()
    {
        // Arrange: no document

        // Act and assert: refused
        Assert.Throws<ArgumentNullException>(() => new AttestationRenderer().Render(null!));
    }
}
