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
    ///     What the provider noted about the key while it loaded it (see <see cref="NoteWarning"/>); guarded by <see cref="_gate"/>.
    /// </summary>
    private readonly List<string> _loadWarnings = [];

    /// <summary>
    ///     The expiry warning that was last logged, so that the log shows it when it appears or changes and not on every call.
    /// </summary>
    private string? _loggedExpiryWarning;

    /// <summary>
    ///     Gets the name of the provider.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    ///     Gets a value indicating whether the provider warns that the certificate is about to expire or has expired
    ///     (<see cref="AttestationOptions.ExpiryWarningDays"/>). A provider that replaces an expired key itself says
    ///     so when it does, and returns <c>false</c> so that the operator is not warned twice.
    /// </summary>
    protected virtual bool WarnsAboutExpiry => true;

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
                _loadWarnings.Clear();
                var key = Load();
                foreach (var warning in AttestationKeyPolicy.Warnings(key.Certificate))
                {
#pragma warning disable CA1848 // Start-up diagnostics are logged once; delegates would add noise
                    Log.LogWarning("Attestation signing key {Thumbprint}: {Warning}", key.Info.Thumbprint, warning);
#pragma warning restore CA1848
                }

#pragma warning disable CA1848 // Logged once when the key is found
                if (Log.IsEnabled(LogLevel.Information))
                {
                    Log.LogInformation(
                        "Attestation signing key from provider {Provider}: subject {Subject}, thumbprint {Thumbprint}, self-signed {SelfSigned}.",
                        key.Info.Provider,
                        key.Info.Subject,
                        key.Info.Thumbprint,
                        key.Info.IsSelfSigned);
                }
#pragma warning restore CA1848
                if (key.Info.IsSelfSigned)
                {
#pragma warning disable CA1848 // Logged once when the key is found
                    Log.LogWarning(
                        "The attestation signing key is self-signed: for development and test only. "
                        + "Set Attestation:Provider to CertificateStore or File to sign with an organization key.");
#pragma warning restore CA1848
                }

                _key = key;
            }

            // The policy is checked on every call because validity changes with time
            var now = Time.GetUtcNow();
            AttestationKeyPolicy.Enforce(_key.Info, Options.RequireTrustedKey, now);
            LogExpiryWarning(_key.Info, now);
            return _key;
        }
    }

    /// <inheritdoc />
    public AttestationKeyStatus Describe()
    {
        try
        {
            var info = GetSigningKey().Info;
            return new AttestationKeyStatus(Name, true, info, null) { Warnings = CollectWarnings(info) };
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

    /// <summary>
    ///     Records and logs something an operator should know about the key that <see cref="Load"/> is finding, such
    ///     as a key file that other users can read. The warning is part of <see cref="AttestationKeyStatus.Warnings"/>.
    /// </summary>
    /// <param name="warning">The warning; it must hold no secret.</param>
    protected void NoteWarning(string warning)
    {
        _loadWarnings.Add(warning);
#pragma warning disable CA1848 // Start-up diagnostics are logged once; delegates would add noise
        Log.LogWarning("Attestation signing key of provider {Provider}: {Warning}", Name, warning);
#pragma warning restore CA1848
    }

    /// <summary>
    ///     Gets the expiry warning for a key.
    /// </summary>
    /// <param name="info">The description of the key.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The warning, or <c>null</c> when there is none or the provider does not warn.</returns>
    private string? ExpiryWarning(AttestationKeyInfo info, DateTimeOffset now) =>
        WarnsAboutExpiry ? AttestationKeyPolicy.ExpiryWarning(info, now, Options.ExpiryWarningDays) : null;

    /// <summary>
    ///     Logs the expiry warning when it appears or changes.
    /// </summary>
    /// <param name="info">The description of the key.</param>
    /// <param name="now">The current time.</param>
    private void LogExpiryWarning(AttestationKeyInfo info, DateTimeOffset now)
    {
        var warning = ExpiryWarning(info, now);
        if (warning is not null && warning != _loggedExpiryWarning)
        {
#pragma warning disable CA1848 // Logged only when the warning appears or changes
            Log.LogWarning("Attestation signing key: {Warning}", warning);
#pragma warning restore CA1848
        }

        _loggedExpiryWarning = warning;
    }

    /// <summary>
    ///     Collects the warnings about the loaded key: what the provider noted, and the expiry warning.
    /// </summary>
    /// <param name="info">The description of the key.</param>
    /// <returns>The warnings.</returns>
    private List<string> CollectWarnings(AttestationKeyInfo info)
    {
        lock (_gate)
        {
            var warnings = new List<string>(_loadWarnings);
            if (ExpiryWarning(info, Time.GetUtcNow()) is { } expiry)
            {
                warnings.Add(expiry);
            }

            return warnings;
        }
    }
}
