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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="Fingerprint"/>: the review content fingerprint must match the ReviewMark
///     algorithm bit for bit, so most tests pin known answers.
/// </summary>
public class FingerprintTests
{
    /// <summary>
    ///     Test that the per-file hash of known text matches the known answer.
    /// </summary>
    [Fact]
    public void Fingerprint_ComputeFileHash_KnownText_MatchesKnownAnswer()
    {
        // Arrange: the bytes of "hello\n"
        var content = TestContent.A;

        // Act: hash the file
        var hash = Fingerprint.ComputeFileHash(content);

        // Assert: lowercase hex SHA-256
        Assert.Equal(TestContent.HashA, hash);
    }

    /// <summary>
    ///     Test that binary content with CR LF and bytes invalid in UTF-8 is hashed verbatim.
    /// </summary>
    [Fact]
    public void Fingerprint_ComputeFileHash_BinaryWithCrLf_MatchesKnownAnswer()
    {
        // Arrange: bytes with NUL, 0xFF and a CR LF pair
        var content = TestContent.C;

        // Act: hash the file
        var hash = Fingerprint.ComputeFileHash(content);

        // Assert: nothing was normalized or re-encoded
        Assert.Equal(TestContent.HashC, hash);
    }

    /// <summary>
    ///     Test that line endings are significant: CR LF content hashes differently from LF content.
    /// </summary>
    [Fact]
    public void Fingerprint_ComputeFileHash_CrLfVersusLf_DiffersBecauseNoNormalization()
    {
        // Arrange: the same text with LF and with CR LF endings
        byte[] lf = [0x61, 0x0a];
        byte[] crlf = [0x61, 0x0d, 0x0a];

        // Act: hash both
        var lfHash = Fingerprint.ComputeFileHash(lf);
        var crlfHash = Fingerprint.ComputeFileHash(crlf);

        // Assert: different bytes, different hashes
        Assert.NotEqual(lfHash, crlfHash);
    }

    /// <summary>
    ///     Test that a single-file set matches the known fingerprint.
    /// </summary>
    [Fact]
    public void Fingerprint_Compute_SingleFile_MatchesKnownAnswer()
    {
        // Arrange: a set with one file
        byte[][] contents = [TestContent.A];

        // Act: compute the fingerprint
        var fingerprint = Fingerprint.Compute(contents);

        // Assert: matches the known answer
        Assert.Equal(TestContent.FingerprintA, fingerprint);
    }

    /// <summary>
    ///     Test that a three-file set including binary content matches the known fingerprint.
    /// </summary>
    [Fact]
    public void Fingerprint_Compute_ThreeFilesIncludingBinary_MatchesKnownAnswer()
    {
        // Arrange: text, text and binary content
        byte[][] contents = [TestContent.A, TestContent.B, TestContent.C];

        // Act: compute the fingerprint
        var fingerprint = Fingerprint.Compute(contents);

        // Assert: matches the known answer
        Assert.Equal(TestContent.FingerprintABC, fingerprint);
    }

    /// <summary>
    ///     Test that the fingerprint does not depend on the order the files are given in.
    /// </summary>
    [Fact]
    public void Fingerprint_Compute_DifferentOrder_GivesSameFingerprint()
    {
        // Arrange: the same three files in two orders
        byte[][] forward = [TestContent.A, TestContent.B, TestContent.C];
        byte[][] reversed = [TestContent.C, TestContent.B, TestContent.A];

        // Act: compute both
        var forwardFingerprint = Fingerprint.Compute(forward);
        var reversedFingerprint = Fingerprint.Compute(reversed);

        // Assert: identical, and equal to the known answer
        Assert.Equal(forwardFingerprint, reversedFingerprint);
        Assert.Equal(TestContent.FingerprintABC, reversedFingerprint);
    }

    /// <summary>
    ///     Test that identical content in two files is counted twice rather than collapsed.
    /// </summary>
    [Fact]
    public void Fingerprint_Compute_DuplicateContent_IsNotCollapsed()
    {
        // Arrange: two files with identical content
        byte[][] contents = [TestContent.A, TestContent.A];

        // Act: compute the fingerprint
        var fingerprint = Fingerprint.Compute(contents);

        // Assert: matches the known answer and differs from the single-file set
        Assert.Equal(TestContent.FingerprintAA, fingerprint);
        Assert.NotEqual(TestContent.FingerprintA, fingerprint);
    }

    /// <summary>
    ///     Test that the fingerprint computed from stored per-file hashes equals the one from content.
    /// </summary>
    [Fact]
    public void Fingerprint_Compute_FromFileHashes_EqualsFromContents()
    {
        // Arrange: the per-file hashes of three files
        string[] hashes = [TestContent.HashC, TestContent.HashA, TestContent.HashB];

        // Act: compute from the hashes
        var fingerprint = Fingerprint.Compute(hashes);

        // Assert: same as computing from the bytes
        Assert.Equal(TestContent.FingerprintABC, fingerprint);
    }
}
