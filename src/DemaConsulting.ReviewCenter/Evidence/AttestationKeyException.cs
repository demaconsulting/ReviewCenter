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

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     The problem that stops the attestation signing key from being used.
/// </summary>
/// <remarks>
///     The message is meant for an administrator and for the page that shows why attestation is unavailable. It names
///     the <c>Attestation:*</c> setting to correct and never contains a secret; it may name a folder or a file, which
///     is fine for the server log but not for the configuration check, which uses <see cref="MessageWithoutPath"/>.
///     Two flags let a caller that only inspects the key (the configuration check) tell the two expected situations
///     from a real failure.
/// </remarks>
/// <param name="message">The user-safe message.</param>
/// <param name="inner">The underlying error, or <c>null</c>.</param>
public sealed class AttestationKeyException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>
    ///     Gets a value indicating whether the key does not exist yet and the provider was told not to create it; the
    ///     server creates it at its first start.
    /// </summary>
    public bool WillBeCreated { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the key exists but cannot be decrypted or read, typically because the key
    ///     ring that protected it is not the one in use.
    /// </summary>
    public bool IsUnreadable { get; init; }

    /// <summary>
    ///     Gets a message that names no folder and no path, for a report that must not print one (the configuration
    ///     check), or <c>null</c> when <see cref="Exception.Message"/> already contains none.
    /// </summary>
    public string? MessageWithoutPath { get; init; }
}
