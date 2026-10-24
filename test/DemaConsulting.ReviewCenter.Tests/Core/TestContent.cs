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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Known file contents and the independently verified hashes computed from them.
/// </summary>
/// <remarks>
///     The bytes are written out in source (never read from a text file) so that source control line-ending
///     conversion cannot change what is hashed. The expected hashes were verified against the ReviewMark
///     1.4.0 implementation of the same algorithm.
/// </remarks>
internal static class TestContent
{
    /// <summary>
    ///     The ASCII text <c>hello</c> followed by a line feed.
    /// </summary>
    public static byte[] A => [0x68, 0x65, 0x6c, 0x6c, 0x6f, 0x0a];

    /// <summary>
    ///     The ASCII text <c>world</c> followed by a line feed.
    /// </summary>
    public static byte[] B => [0x77, 0x6f, 0x72, 0x6c, 0x64, 0x0a];

    /// <summary>
    ///     Binary content with a NUL byte, an invalid UTF-8 byte, and a CR LF pair.
    /// </summary>
    public static byte[] C => [0x00, 0x01, 0x02, 0xff, 0x0d, 0x0a, 0x0a];

    /// <summary>
    ///     Expected SHA-256 of <see cref="A"/>.
    /// </summary>
    public const string HashA = "5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03";

    /// <summary>
    ///     Expected SHA-256 of <see cref="B"/>.
    /// </summary>
    public const string HashB = "e258d248fda94c63753607f7c4494ee0fcbe92f1a76bfdac795c9d84101eb317";

    /// <summary>
    ///     Expected SHA-256 of <see cref="C"/>.
    /// </summary>
    public const string HashC = "57361c6e2882a06baeed989a104e01c40e5846ba7c12e044588c1deb866836b5";

    /// <summary>
    ///     Expected SHA-256 of zero bytes (the well-known hash of empty content).
    /// </summary>
    public const string HashEmpty = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    /// <summary>
    ///     Expected fingerprint of the set {A}.
    /// </summary>
    public const string FingerprintA = "7f39224e335994886c26ba8c241fcbe1d474aadaa2bd0a8e842983b098cea894";

    /// <summary>
    ///     Expected fingerprint of the set {A, B, C}.
    /// </summary>
    public const string FingerprintABC = "14aff6b60cf429a3eaeb2d20b71aae5b4fba2976af21766472747e130d9b73e9";

    /// <summary>
    ///     Expected fingerprint of the set {A, A}.
    /// </summary>
    public const string FingerprintAA = "71454b11b09343ed4b9ad176cc726f9cf9684439335436d874fdb70a026d99f5";
}
