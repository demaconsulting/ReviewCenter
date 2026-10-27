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

using Microsoft.Extensions.Logging;

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     Chooses the key provider from the <c>Attestation:Provider</c> setting.
/// </summary>
internal static class AttestationKeyProviderFactory
{
    /// <summary>
    ///     Creates the provider that the settings name.
    /// </summary>
    /// <param name="options">The attestation settings.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logs">The logger factory.</param>
    /// <returns>The provider. It is disposable (it owns the key) and the caller keeps it for the life of the application.</returns>
    /// <exception cref="AttestationKeyException">Thrown when the provider is not supported.</exception>
    public static IAttestationKeyProvider Create(AttestationOptions options, TimeProvider time, ILoggerFactory logs)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logs);

        // A blank setting means the default
        var name = string.IsNullOrWhiteSpace(options.Provider) ? AttestationOptions.SelfSignedProvider : options.Provider.Trim();
        if (string.Equals(name, AttestationOptions.SelfSignedProvider, StringComparison.OrdinalIgnoreCase))
        {
            return new SelfSignedKeyProvider(options, time, logs.CreateLogger<SelfSignedKeyProvider>());
        }

        if (string.Equals(name, AttestationOptions.CertificateStoreProvider, StringComparison.OrdinalIgnoreCase))
        {
            return new CertificateStoreKeyProvider(options, time, logs.CreateLogger<CertificateStoreKeyProvider>());
        }

        throw new AttestationKeyException(
            $"Attestation:Provider '{name}' is not supported. Use {AttestationOptions.SelfSignedProvider} or {AttestationOptions.CertificateStoreProvider}.");
    }
}
