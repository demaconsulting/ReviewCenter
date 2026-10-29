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
using DemaConsulting.ReviewCenter.Utilities;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     Checks that the content fingerprint of a fixed set of files is the same value on every platform.
/// </summary>
/// <remarks>
///     The fingerprint is what ReviewMark compares, so a platform difference (line endings, text encoding, sort
///     order) would silently invalidate every review. The check writes three files whose exact bytes are fixed,
///     hashes them with the production <see cref="Fingerprint"/> and compares each hash and the combined
///     fingerprint with literal values that were computed once, independently, and are part of this source. The
///     files live in a temporary folder. The class is stateless and thread-safe.
/// </remarks>
internal static class FingerprintCheck
{
    /// <summary>
    ///     The name of the check in the report and the results file.
    /// </summary>
    public const string Name = "ReviewCenter_Fingerprint";

    /// <summary>
    ///     The combined fingerprint of the three files, pinned as a literal.
    /// </summary>
    public const string ExpectedFingerprint = "13e028da06299fcedffff47b2b55cca46a68111f3124d77d355364339c4215f2";

    /// <summary>
    ///     The files, their exact content and the pinned SHA-256 of each.
    /// </summary>
    private static readonly (string Path, string Content, string Sha256)[] Files =
    [
        ("a.md", "# Title\n", "e01b17ff9af77056792f67c57e3d1908795b9d1ae4cfe72421d0a2838991b740"),
        ("b.cs", "class A {}\r\n", "83f23308fc99b44c3b8d187622dab54653555919f62fb41491358231d9c9e7e0"),
        ("c.txt", "", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
    ];

    /// <summary>
    ///     Runs the check.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The fingerprint that was computed.</returns>
    /// <exception cref="ValidationFailedException">Thrown when a hash or the fingerprint differs from the pinned value.</exception>
    public static Task<string> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var folder = new TemporaryDirectory();

        // Exact bytes: UTF-8 without a byte-order mark, line endings as written
        var hashes = new List<string>();
        foreach (var (path, content, expected) in Files)
        {
            var file = folder.GetFilePath(path);
            File.WriteAllBytes(file, new UTF8Encoding(false).GetBytes(content));
            var hash = Fingerprint.ComputeFileHash(File.ReadAllBytes(file));
            ValidationFailedException.ThrowUnless(
                hash == expected, $"The hash of '{path}' is {hash}, not the pinned {expected}.");
            hashes.Add(hash);
        }

        var fingerprint = Fingerprint.Compute(hashes);
        ValidationFailedException.ThrowUnless(
            fingerprint == ExpectedFingerprint,
            $"The fingerprint is {fingerprint}, not the pinned {ExpectedFingerprint}.");
        return Task.FromResult($"{Files.Length} files, fingerprint {fingerprint}");
    }
}
