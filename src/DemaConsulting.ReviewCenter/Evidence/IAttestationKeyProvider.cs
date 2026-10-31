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
///     Whether the attestation signing key can be used right now, for the page that offers the download.
/// </summary>
/// <param name="Provider">The name of the configured provider.</param>
/// <param name="IsAvailable">Whether a key was found and satisfies the trust policy.</param>
/// <param name="Info">The public description of the key, or <c>null</c> when it is not available.</param>
/// <param name="Problem">The reason the key is not available, or <c>null</c> when it is.</param>
/// <remarks>
///     <see cref="Warnings"/> lists what an operator should know about a key that is available and works: the
///     certificate is about to expire or has expired, the key file is readable by other users, the key is not
///     encrypted. It is empty for a key that is not available and for a key without anything to say.
/// </remarks>
internal sealed record AttestationKeyStatus(string Provider, bool IsAvailable, AttestationKeyInfo? Info, string? Problem)
{
    /// <summary>
    ///     Gets the warnings about the key; empty when there are none.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
///     Supplies the key that signs attestations; the only thing the signer, the service and the page know about
///     where the key lives.
/// </summary>
internal interface IAttestationKeyProvider
{
    /// <summary>
    ///     Gets the name of the provider, such as <c>SelfSigned</c> or <c>CertificateStore</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Gets the signing key. A key that was found is kept for later calls; the trust policy is checked on every call.
    /// </summary>
    /// <returns>The signing key. It is owned by the provider; callers must not dispose it.</returns>
    /// <exception cref="AttestationKeyException">Thrown when there is no usable key. A failure is not remembered.</exception>
    AttestationSigningKey GetSigningKey();

    /// <summary>
    ///     Describes the state of the key without throwing.
    /// </summary>
    /// <returns>The status; <see cref="AttestationKeyStatus.IsAvailable"/> is false when <see cref="GetSigningKey"/> would throw.</returns>
    AttestationKeyStatus Describe();
}
