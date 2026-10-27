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
///     The common part of the key providers: the key is loaded once and kept, access is serialized, the trust policy
///     is enforced on every call, and the key that was found is logged.
/// </summary>
/// <remarks>
///     A provider only has to find the key (<see cref="Load"/>). Everything that must be the same for every provider,
///     in particular the <c>Attestation:RequireTrustedKey</c> policy, lives here so that a new provider cannot
///     forget it. The class is thread-safe. Nothing secret is ever logged.
/// </remarks>
/// <param name="options">The attestation settings.</param>
/// <param name="time">The clock used for the validity check.</param>
/// <param name="log">The logger.</param>
internal abstract class AttestationKeyProviderBase(AttestationOptions options, TimeProvider time, ILogger log)
    : IAttestationKeyProvider, IDisposable
{
    /// <summary>
    ///     Guards <see cref="_key"/>.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     The key once it was loaded, or <c>null</c> before that.
    /// </summary>
    private AttestationSigningKey? _key;

    /// <summary>
    ///     Gets the name of the provider.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    ///     Gets the attestation settings the provider was created with.
    /// </summary>
    protected AttestationOptions Options { get; } = options;

    /// <summary>
    ///     Gets the clock the provider was created with.
    /// </summary>
    protected TimeProvider Time { get; } = time;

    /// <summary>
    ///     Gets the logger of the provider.
    /// </summary>
    protected ILogger Log { get; } = log;

    /// <inheritdoc />
    public AttestationSigningKey GetSigningKey()
    {
        lock (_gate)
        {
            if (_key is null)
            {
                // Find the key once; a failure leaves nothing behind, so the next call looks again
                var key = Load();
                foreach (var warning in AttestationKeyPolicy.Warnings(key.Certificate))
                {
#pragma warning disable CA1848 // Start-up diagnostics are logged once; delegates would add noise
                    Log.LogWarning("Attestation signing key {Thumbprint}: {Warning}", key.Info.Thumbprint, warning);
#pragma warning restore CA1848
                }

#pragma warning disable CA1848 // Logged once when the key is found
                Log.LogInformation(
                    "Attestation signing key from provider {Provider}: subject {Subject}, thumbprint {Thumbprint}, self-signed {SelfSigned}.",
                    key.Info.Provider,
                    key.Info.Subject,
                    key.Info.Thumbprint,
                    key.Info.IsSelfSigned);
#pragma warning restore CA1848
                if (key.Info.IsSelfSigned)
                {
#pragma warning disable CA1848 // Logged once when the key is found
                    Log.LogWarning(
                        "The attestation signing key is self-signed: for development and test only. "
                        + "Set Attestation:Provider to CertificateStore to sign with an organization key.");
#pragma warning restore CA1848
                }

                _key = key;
            }

            // The policy is checked on every call because validity changes with time
            AttestationKeyPolicy.Enforce(_key.Info, Options.RequireTrustedKey, Time.GetUtcNow());
            return _key;
        }
    }

    /// <inheritdoc />
    public AttestationKeyStatus Describe()
    {
        try
        {
            return new AttestationKeyStatus(Name, true, GetSigningKey().Info, null);
        }
        catch (AttestationKeyException ex)
        {
            return new AttestationKeyStatus(Name, false, null, ex.Message);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // An unexpected failure of the platform (a malformed folder or store name, a broken certificate chain, ...)
            // must not break the page that asks: report the key as unavailable with a message that holds no detail,
            // and keep the detail in the log
#pragma warning disable CA1848 // Logged only for an unexpected failure
            Log.LogWarning(ex, "The attestation signing key of provider {Provider} could not be loaded.", Name);
#pragma warning restore CA1848
            return new AttestationKeyStatus(
                Name,
                false,
                null,
                "The attestation signing key could not be loaded because of an unexpected error; see the server log.");
        }
    }

    /// <summary>
    ///     Releases the key.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Releases the key.
    /// </summary>
    /// <param name="disposing"><c>true</c> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        lock (_gate)
        {
            _key?.Dispose();
            _key = null;
        }
    }

    /// <summary>
    ///     Finds the key. Called once; its result is kept.
    /// </summary>
    /// <returns>The signing key, created with <see cref="AttestationOptions.RequireTrustedKey"/>.</returns>
    /// <exception cref="AttestationKeyException">Thrown, with a message that names the setting to correct, when there is no key.</exception>
    protected abstract AttestationSigningKey Load();
}
