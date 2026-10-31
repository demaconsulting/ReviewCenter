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

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Xml;
using System.Xml.Linq;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Hosting;

/// <summary>
///     The settings of the persisted Data Protection key ring, bound from the <c>DataProtection</c> section.
/// </summary>
/// <remarks>
///     The key ring encrypts repository tokens and the secrets of the second factor, and also signs the sign-in
///     cookie and the antiforgery tokens. Losing it makes the stored tokens unreadable (administrators must enter
///     them again) and signs everybody out once. The choice values (<see cref="Protection"/> and
///     <see cref="DpapiScope"/>) are kept as text and checked by <see cref="KeyRingProtection"/>, so that a wrong
///     value is reported with a message that names the setting instead of failing the binding. The class is a plain
///     settings holder that is only read after start-up. <see cref="CertificatePassword"/> is a secret: it is never
///     logged or put in a message, and the shipped <c>appsettings.json</c> does not contain it.
/// </remarks>
internal sealed class KeyRingSettings
{
    /// <summary>
    ///     The name of the configuration section the settings are bound from.
    /// </summary>
    public const string SectionName = "DataProtection";

    /// <summary>
    ///     Gets or sets the folder that holds the key ring files, or <c>null</c> or empty for the default folder,
    ///     <c>keys</c> beside the database.
    /// </summary>
    public string? KeysPath { get; set; }

    /// <summary>
    ///     Gets or sets how the key files are protected: <c>Auto</c>, <c>Dpapi</c>, <c>Certificate</c> or
    ///     <c>PermissionsOnly</c>, in any case; <c>null</c> or empty means <c>Auto</c>.
    /// </summary>
    public string? Protection { get; set; }

    /// <summary>
    ///     Gets or sets whose DPAPI key protects the key files: <c>CurrentUser</c> or <c>LocalMachine</c>, in any case;
    ///     <c>null</c> or empty means <c>CurrentUser</c>. Only used by the <c>Dpapi</c> mode, which is Windows only.
    /// </summary>
    public string? DpapiScope { get; set; }

    /// <summary>
    ///     Gets or sets the path of the certificate whose public key encrypts the key files: a PKCS #12 file (PFX or
    ///     P12) or a PEM file; <c>null</c> or empty for none.
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    ///     Gets or sets the path of a separate PEM file with the private key of the certificate, when it is not in
    ///     the file of <see cref="CertificatePath"/>; <c>null</c> or empty for none.
    /// </summary>
    public string? CertificateKeyPath { get; set; }

    /// <summary>
    ///     Gets or sets the password of the certificate file, inline; a secret. <c>null</c> or empty for none.
    /// </summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    ///     Gets or sets the path of a file that holds the password of the certificate file; <c>null</c> or empty for
    ///     none. Setting both this and <see cref="CertificatePassword"/> is refused.
    /// </summary>
    public string? CertificatePasswordFile { get; set; }

    /// <summary>
    ///     Gets or sets the paths of earlier certificates that may still be needed to read key files that were
    ///     encrypted with them; they are only used for decryption.
    /// </summary>
    public List<string> PreviousCertificatePaths { get; set; } = [];

    /// <summary>
    ///     Gets or sets a value indicating whether the server refuses to start when the key ring is not encrypted at
    ///     rest, that is, in the <c>PermissionsOnly</c> mode or when a key file of the ring is not encrypted.
    /// </summary>
    public bool RequireEncryptedKeys { get; set; }
}

/// <summary>
///     The operating system as far as the key protection decision needs it; a value that tests supply so that every
///     platform's decision can be checked on any machine.
/// </summary>
/// <param name="IsWindows">Whether Windows DPAPI is available.</param>
/// <param name="IsUnix">Whether the file system has Unix permissions that can be enforced.</param>
internal readonly record struct KeyProtectionPlatform(bool IsWindows, bool IsUnix)
{
    /// <summary>
    ///     Gets the platform the process runs on.
    /// </summary>
    public static KeyProtectionPlatform Current { get; } = new(
        OperatingSystem.IsWindows(),
        OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD());
}

/// <summary>
///     The outcome of <see cref="KeyRingProtection.Prepare(IConfiguration, KeyProtectionPlatform)"/>: the mode that
///     applies, the certificates that were loaded for it, and the problem that stops the start, if there is one.
/// </summary>
/// <remarks>
///     The plan is made when the services are registered and registered itself as a service, so that the start-up
///     checks and the wiring see exactly the same decision. It owns the certificates and disposes them with the
///     service provider. A plan with a <see cref="Problem"/> has no certificates and wires nothing; the start-up
///     check reports the problem. Instances are immutable after construction, so they are thread-safe.
/// </remarks>
internal sealed class KeyRingPlan : IDisposable
{
    /// <summary>
    ///     The certificates the plan owns.
    /// </summary>
    private readonly List<X509Certificate2> _owned = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="KeyRingPlan"/> class.
    /// </summary>
    /// <param name="mode">The resolved mode; never <see cref="KeyRingProtectionMode.Auto"/>.</param>
    /// <param name="localMachine">Whether DPAPI protects to the machine rather than the current user.</param>
    /// <param name="requireEncryptedKeys">Whether an unencrypted key ring stops the start.</param>
    /// <param name="certificate">The certificate that encrypts the key files; <c>null</c> unless the mode needs it.</param>
    /// <param name="previous">The earlier certificates that only decrypt.</param>
    /// <param name="problem">The message that stops the start; <c>null</c> when the settings are usable.</param>
    public KeyRingPlan(
        KeyRingProtectionMode mode,
        bool localMachine,
        bool requireEncryptedKeys,
        X509Certificate2? certificate,
        IReadOnlyList<X509Certificate2> previous,
        string? problem)
    {
        Mode = mode;
        LocalMachine = localMachine;
        RequireEncryptedKeys = requireEncryptedKeys;
        Certificate = certificate;
        Previous = previous;
        Problem = problem;
        if (certificate is not null)
        {
            _owned.Add(certificate);
        }

        _owned.AddRange(previous);
    }

    /// <summary>
    ///     Gets the resolved mode. Meaningless when <see cref="Problem"/> is set.
    /// </summary>
    public KeyRingProtectionMode Mode { get; }

    /// <summary>
    ///     Gets a value indicating whether DPAPI protects to the machine rather than the current user.
    /// </summary>
    public bool LocalMachine { get; }

    /// <summary>
    ///     Gets a value indicating whether an unencrypted key ring stops the start.
    /// </summary>
    public bool RequireEncryptedKeys { get; }

    /// <summary>
    ///     Gets the certificate that encrypts the key files, or <c>null</c> when the mode does not use one.
    /// </summary>
    public X509Certificate2? Certificate { get; }

    /// <summary>
    ///     Gets the earlier certificates that are only used to decrypt key files.
    /// </summary>
    public IReadOnlyList<X509Certificate2> Previous { get; }

    /// <summary>
    ///     Gets the message that stops the start, or <c>null</c> when the settings are usable. The message names the
    ///     setting at fault and never holds a secret.
    /// </summary>
    public string? Problem { get; }

    /// <summary>
    ///     Disposes the certificates the plan owns.
    /// </summary>
    public void Dispose()
    {
        foreach (var certificate in _owned)
        {
            certificate.Dispose();
        }

        _owned.Clear();
    }
}

/// <summary>
///     Protects the Data Protection key ring at rest: turns the <c>DataProtection</c> settings into a decision, wires
///     the encryption of the key files into Data Protection, and checks the ring before the server uses it.
/// </summary>
/// <remarks>
///     Everything the product encrypts (repository tokens, second-factor secrets, cookies) goes through Data
///     Protection, so the key ring is the one root of trust, and protecting its files at rest protects all of them.
///     The unit offers three modes. Windows DPAPI binds the key files to the account or machine of the server and
///     needs no setup. A certificate that the operator supplies as a file encrypts them on every platform, and is
///     the way to a ring that can be restored on other hardware. Owner-only permissions are all that is left where
///     the operator supplies no secret, because the operating system has nothing that .NET can use to bind keys to a
///     machine and a key stored beside the ring would be theater; that mode is reported with a warning at every
///     start. The decision is a pure function of the settings and a <see cref="KeyProtectionPlatform"/>, so tests
///     cover every platform on any machine. A ring that cannot be decrypted is the dangerous case: Data Protection
///     does not fail but quietly creates a second key, and every stored secret is lost. <see cref="CheckRing"/>
///     therefore reads the ring (without creating a key) before anything is protected and refuses to start. Data
///     Protection reads encrypted and unencrypted key files alike, so a ring from before the protection stays
///     readable, but a key that is stored unencrypted stays so until it is retired. The unit holds no state; the
///     plan carries the certificates.
/// </remarks>
internal static class KeyRingProtection
{
    /// <summary>
    ///     The text that every message about unusable settings starts with.
    /// </summary>
    private const string InvalidPrefix = "Invalid key ring settings: ";

    /// <summary>
    ///     The text of an unencrypted key in a key file; an encrypted key file holds an <c>encryptedSecret</c>
    ///     element instead, which hides this element.
    /// </summary>
    private const string PlaintextMarker = "<masterKey";

    /// <summary>
    ///     The names of the key files of a Data Protection key ring.
    /// </summary>
    private const string KeyFilePattern = "key-*.xml";

    /// <summary>
    ///     Chooses the mode that applies to the settings on a platform.
    /// </summary>
    /// <param name="settings">The settings. Must not be null.</param>
    /// <param name="platform">The platform to decide for.</param>
    /// <returns>
    ///     One of <see cref="KeyRingProtectionMode.Dpapi"/>, <see cref="KeyRingProtectionMode.Certificate"/> or
    ///     <see cref="KeyRingProtectionMode.PermissionsOnly"/>. <c>Auto</c> becomes a certificate when
    ///     <c>DataProtection:CertificatePath</c> is set (an operator who supplies a certificate wants it used on any
    ///     platform), otherwise DPAPI on Windows and owner-only permissions elsewhere.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the mode is not one of the four, when <c>Dpapi</c> is requested off Windows, or when
    ///     <c>Certificate</c> is requested without a certificate path. The message names the setting.
    /// </exception>
    internal static KeyRingProtectionMode ResolveMode(KeyRingSettings settings, KeyProtectionPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // An explicit choice is taken as it is, once it is checked; only Auto is decided here
        var requested = ParseChoice(
            settings.Protection,
            KeyRingProtectionMode.Auto,
            "DataProtection:Protection",
            "Auto, Dpapi, Certificate or PermissionsOnly");
        var hasCertificate = !string.IsNullOrWhiteSpace(settings.CertificatePath);
        switch (requested)
        {
            case KeyRingProtectionMode.Dpapi when !platform.IsWindows:
                throw new InvalidOperationException("DataProtection:Protection Dpapi is only available on Windows.");
            case KeyRingProtectionMode.Certificate when !hasCertificate:
                throw new InvalidOperationException(
                    "DataProtection:CertificatePath is not set; it is required when DataProtection:Protection is Certificate.");
            case KeyRingProtectionMode.Auto when hasCertificate:
                return KeyRingProtectionMode.Certificate;
            case KeyRingProtectionMode.Auto:
                return platform.IsWindows ? KeyRingProtectionMode.Dpapi : KeyRingProtectionMode.PermissionsOnly;
            default:
                return requested;
        }
    }

    /// <summary>
    ///     Reads the <c>DataProtection</c> section and makes the plan for it; never throws for a bad setting.
    /// </summary>
    /// <param name="configuration">The configuration that holds the <c>DataProtection</c> section. Must not be null.</param>
    /// <param name="platform">The platform to decide for.</param>
    /// <returns>The plan. The caller owns it and must dispose it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is null.</exception>
    internal static KeyRingPlan Prepare(IConfiguration configuration, KeyProtectionPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // A value that cannot be bound (a boolean that is not one) is a setting problem like any other
        KeyRingSettings settings;
        try
        {
            settings = configuration.GetSection(KeyRingSettings.SectionName).Get<KeyRingSettings>() ?? new KeyRingSettings();
        }
        catch (InvalidOperationException ex)
        {
            return Rejected(ex.Message);
        }

        return Prepare(settings, platform);
    }

    /// <summary>
    ///     Makes the plan for the settings: decides the mode and loads the certificates it needs.
    /// </summary>
    /// <param name="settings">The settings. Must not be null.</param>
    /// <param name="platform">The platform to decide for.</param>
    /// <returns>
    ///     The plan. A setting that cannot be used does not throw: the plan carries the message in
    ///     <see cref="KeyRingPlan.Problem"/> so that the start-up check reports it. The caller owns the plan.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
    /// <remarks>
    ///     Reading the password goes through <see cref="SecretSource"/>, so the inline and the file form follow the
    ///     rules of every secret of the product, and a message never holds the password. The password is only read
    ///     when a certificate is needed.
    /// </remarks>
    internal static KeyRingPlan Prepare(KeyRingSettings settings, KeyProtectionPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var loaded = new List<X509Certificate2>();
        try
        {
            // Decide first: every message about the choice comes before any file is touched
            var mode = ResolveMode(settings, platform);
            var scope = ParseChoice(
                settings.DpapiScope,
                DpapiScopeChoice.CurrentUser,
                "DataProtection:DpapiScope",
                "CurrentUser or LocalMachine");
            if (mode == KeyRingProtectionMode.PermissionsOnly && settings.RequireEncryptedKeys)
            {
                throw new InvalidOperationException(
                    "The key ring is not encrypted at rest and DataProtection:RequireEncryptedKeys is true. " +
                    "Set DataProtection:CertificatePath to encrypt it, or use Windows with DataProtection:Protection Dpapi.");
            }

            // The certificates: the one that encrypts, and the earlier ones that may still decrypt
            var previousPaths = settings.PreviousCertificatePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList();
            string? password = null;
            if (mode == KeyRingProtectionMode.Certificate || previousPaths.Count > 0)
            {
                password = SecretSource.Resolve(
                    settings.CertificatePassword,
                    "DataProtection:CertificatePassword",
                    settings.CertificatePasswordFile,
                    "DataProtection:CertificatePasswordFile");
            }

            X509Certificate2? certificate = null;
            if (mode == KeyRingProtectionMode.Certificate)
            {
                certificate = LoadCertificate(
                    settings.CertificatePath!,
                    settings.CertificateKeyPath,
                    password,
                    "DataProtection:CertificatePath");
                loaded.Add(certificate);
            }

            for (var index = 0; index < previousPaths.Count; index++)
            {
                loaded.Add(LoadPrevious(previousPaths[index], password, index));
            }

            var previous = loaded.Where(candidate => !ReferenceEquals(candidate, certificate)).ToList();
            return new KeyRingPlan(
                mode,
                scope == DpapiScopeChoice.LocalMachine,
                settings.RequireEncryptedKeys,
                certificate,
                previous,
                problem: null);
        }
        catch (InvalidOperationException ex)
        {
            // Nothing is wired for a plan that is refused, so the certificates loaded so far are released now
            foreach (var certificate in loaded)
            {
                certificate.Dispose();
            }

            return Rejected(ex.Message);
        }
    }

    /// <summary>
    ///     Wires the encryption of the key files into Data Protection, and the decryption with the earlier
    ///     certificates.
    /// </summary>
    /// <param name="builder">The Data Protection builder. Must not be null.</param>
    /// <param name="plan">The plan. Must not be null.</param>
    /// <param name="dpapiFactory">
    ///     Creates the DPAPI encryptor from the scope (<c>true</c> for the machine) and a logger factory; <c>null</c>
    ///     selects the real one. Tests pass a recording factory so that the choice of DPAPI is checked without
    ///     calling DPAPI.
    /// </param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="plan"/> is null.</exception>
    /// <remarks>
    ///     A plan with a problem wires nothing: the start-up check stops the start before the key ring is used. The
    ///     encryptor is created lazily, when the key manager first needs it. Encrypting is never called here.
    ///     Certificate encryption and the earlier certificates use the builder extensions of Data Protection, which
    ///     also register the decryption: reading an encrypted key file needs a certificate that has its private key.
    /// </remarks>
    internal static IDataProtectionBuilder Apply(
        IDataProtectionBuilder builder,
        KeyRingPlan plan,
        Func<bool, ILoggerFactory, IXmlEncryptor>? dpapiFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Problem is not null)
        {
            return builder;
        }

        switch (plan.Mode)
        {
            case KeyRingProtectionMode.Dpapi:
                var factory = dpapiFactory ?? CreateDpapiEncryptor;
                var machine = plan.LocalMachine;
                builder.Services.AddOptions<KeyManagementOptions>()
                    .Configure<ILoggerFactory>((keys, loggerFactory) => keys.XmlEncryptor = factory(machine, loggerFactory));
                break;
            case KeyRingProtectionMode.Certificate:
                builder.ProtectKeysWithCertificate(plan.Certificate!);
                break;
        }

        if (plan.Previous.Count > 0)
        {
            builder.UnprotectKeysWithAnyCertificate([.. plan.Previous]);
        }

        return builder;
    }

    /// <summary>
    ///     Checks the key ring before anything is protected, so that a ring that cannot be read stops the start
    ///     instead of being replaced by a new one.
    /// </summary>
    /// <param name="plan">The plan. Must not be null.</param>
    /// <param name="keys">The key manager of the application. Must not be null.</param>
    /// <param name="folder">The folder of the key ring. Must not be null.</param>
    /// <param name="logger">The logger for the finding about older keys. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the ring holds keys and the newest key (or every key) cannot be decrypted, or when
    ///     <c>DataProtection:RequireEncryptedKeys</c> is set and a key file of the ring is not encrypted.
    /// </exception>
    /// <remarks>
    ///     The check reads the keys with <see cref="IKeyManager.GetAllKeys"/>, which creates nothing, and touches the
    ///     descriptor of each: a key that cannot be decrypted (wrong or missing certificate, a ring that DPAPI
    ///     protected for another account or machine) fails there. Data Protection itself would log a warning and
    ///     create a second key in the same folder, and every stored token and second-factor secret would be lost
    ///     without any error. This must run before the probe that protects a value, because that creates a key. An
    ///     empty ring is fine: it is new. When only keys older than the newest cannot be decrypted, the server can
    ///     work, and the finding is logged as an error.
    /// </remarks>
    internal static void CheckRing(KeyRingPlan plan, IKeyManager keys, string folder, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(logger);

        // Unencrypted key files are only a problem when the operator asked for encrypted keys
        if (plan.RequireEncryptedKeys)
        {
            var plaintext = FindPlaintextKeyFiles(folder);
            if (plaintext.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The key ring '{folder}' holds key files that are not encrypted ({string.Join(", ", plaintext)}) and " +
                    "DataProtection:RequireEncryptedKeys is true. Move them out of the folder to retire them (what they " +
                    "protected becomes unreadable), or start with a key ring that was created with encryption.");
            }
        }

        // Reading the keys is read-only; an empty ring is a new one, which is fine
        var all = keys.GetAllKeys();
        if (all.Count == 0)
        {
            return;
        }

        var unreadable = all.Where(key => !CanDecrypt(key)).ToList();
        if (unreadable.Count == 0)
        {
            return;
        }

        // The newest key (by creation) is the one that would protect the next value: when it cannot be read, Data
        // Protection would create another key and carry on, so this is refused. The same goes for a ring of which
        // nothing can be read
        var newest = all.MaxBy(key => key.CreationDate)!;
        if (unreadable.Count == all.Count || unreadable.Contains(newest))
        {
            throw new InvalidOperationException(
                $"The key ring '{folder}' holds {all.Count} key(s), but the newest key cannot be decrypted: the " +
                "certificate is wrong or missing, or the keys were protected with Windows DPAPI for another account " +
                "or machine. Starting would silently create a new key and make every stored token and second-factor " +
                "secret unreadable. Set DataProtection:CertificatePath to the certificate that protects the key files " +
                "and list the older ones in DataProtection:PreviousCertificatePaths, or restore the key folder " +
                "that belongs to this database.");
        }

        // Older keys that cannot be read leave what they protected unreadable, but the server can still work
        var ids = string.Join(", ", unreadable.Select(key => key.KeyId));
#pragma warning disable CA1848 // One entry per start; a LoggerMessage delegate adds no value here
        logger.LogError(
            "{Count} older key(s) of the key ring cannot be decrypted ({KeyIds}); tokens and secrets protected with them " +
            "are unreadable. Add the certificate that protected them to DataProtection:PreviousCertificatePaths",
            unreadable.Count,
            ids);
#pragma warning restore CA1848
    }

    /// <summary>
    ///     Makes the key files readable and writable by their owner only, on a platform with Unix permissions.
    /// </summary>
    /// <param name="folder">The folder of the key ring. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when a permission cannot be set; the message names the folder and <c>DataProtection:KeysPath</c>.
    /// </exception>
    /// <remarks>
    ///     Data Protection creates a key file with the default permissions of the process, which are usually readable
    ///     by the group and everybody. The folder is already restricted to its owner, which keeps others out; the
    ///     files are restricted as well, so that a copy of the folder or a looser folder does not expose them. This
    ///     pass covers the files that exist at start-up; the files that Data Protection creates while the server runs
    ///     (the ring rolls every 90 days) are restricted as they are created by
    ///     <see cref="KeyFileRestrictingXmlRepository"/>. Does nothing on Windows.
    /// </remarks>
    internal static void RestrictKeyFiles(string folder) =>
        RestrictKeyFiles(folder, KeyProtectionPlatform.Current, SetUnixFileMode);

    /// <summary>
    ///     Makes the key files readable and writable by their owner only, with the operating system and the call that
    ///     sets the mode supplied by the caller.
    /// </summary>
    /// <param name="folder">The folder of the key ring. Must not be null.</param>
    /// <param name="platform">The platform; nothing is done unless it has Unix permissions.</param>
    /// <param name="setMode">The call that sets the mode of a file; a test supplies a recording one. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder"/> or <paramref name="setMode"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when a permission cannot be set; the message names the folder and <c>DataProtection:KeysPath</c>.
    /// </exception>
    internal static void RestrictKeyFiles(string folder, KeyProtectionPlatform platform, Action<string, UnixFileMode> setMode)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(setMode);
        if (!platform.IsUnix)
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.xml"))
            {
                setMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The permissions of the key files in '{folder}' cannot be restricted to the server account: {ex.Message} " +
                "Set DataProtection:KeysPath to a folder that the server account owns.",
                ex);
        }
    }

    /// <summary>
    ///     Makes the key folder accessible to its owner and the system only, on Windows, for the
    ///     <c>PermissionsOnly</c> protection.
    /// </summary>
    /// <param name="plan">The plan. Must not be null.</param>
    /// <param name="folder">The folder of the key ring; it is created when it does not exist. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the access rules cannot be set; the message names the folder and <c>DataProtection:KeysPath</c>.
    /// </exception>
    /// <remarks>
    ///     Without a secret to encrypt the key files, the access rules of the folder are all that protects them, and a
    ///     folder inherits the rules of its parent, which usually admit other users. The folder therefore stops
    ///     inheriting and admits only the account of the server and the system (the equivalent of mode 0700 on Unix),
    ///     with rules that the files created later (a new key when the ring rolls) inherit. The call is idempotent.
    ///     Does nothing on another operating system (the permissions there are set by <see cref="RestrictKeyFiles(string)"/>)
    ///     and for the DPAPI and certificate protections, which encrypt the key files themselves.
    /// </remarks>
    internal static void RestrictKeyFolder(KeyRingPlan plan, string folder)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(folder);
        if (!OperatingSystem.IsWindows() || plan.Mode != KeyRingProtectionMode.PermissionsOnly)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            ApplyOwnerOnlyAccess(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"The access rules of the key folder '{folder}' cannot be restricted to the server account: {ex.Message} " +
                "Set DataProtection:KeysPath to a folder that the server account owns.",
                ex);
        }
    }

    /// <summary>
    ///     Replaces the access rules of a folder by full control for the current account and the system, without
    ///     inheritance from the parent and inherited by what is created in the folder.
    /// </summary>
    /// <param name="folder">The existing folder.</param>
    /// <exception cref="InvalidOperationException">Thrown when the account of the process has no identity.</exception>
    [SupportedOSPlatform("windows")]
    private static void ApplyOwnerOnlyAccess(string folder)
    {
        var account = WindowsIdentity.GetCurrent().User
                      ?? throw new InvalidOperationException("The account of the server has no security identifier.");
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        // Stop inheriting without copying the inherited rules, then allow the two accounts for the folder and for
        // everything created in it
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { account, system })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        new DirectoryInfo(folder).SetAccessControl(security);
    }

    /// <summary>
    ///     Sets the mode of a file; the default call of <see cref="RestrictKeyFiles(string)"/>.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="mode">The mode.</param>
    internal static void SetUnixFileMode(string path, UnixFileMode mode)
    {
        // Only called on a platform with Unix permissions; the check is for the platform analyzer
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }
    }

    /// <summary>
    ///     Writes what protects the key ring to the log, once per start.
    /// </summary>
    /// <param name="plan">The plan. Must not be null.</param>
    /// <param name="logger">The logger. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <remarks>
    ///     The <c>PermissionsOnly</c> mode is a warning at every start, because a ring that is not encrypted at rest
    ///     is something an operator should decide on knowingly. No entry holds a secret; a certificate is named by
    ///     its thumbprint, which is public.
    /// </remarks>
    internal static void LogProtection(KeyRingPlan plan, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(logger);

#pragma warning disable CA1848 // One entry per start; a LoggerMessage delegate adds no value here
        switch (plan.Mode)
        {
            case KeyRingProtectionMode.Dpapi:
                logger.LogInformation(
                    "Key ring protected with Windows DPAPI ({Scope})",
                    plan.LocalMachine ? "LocalMachine" : "CurrentUser");
                break;
            case KeyRingProtectionMode.Certificate:
                logger.LogInformation("Key ring protected with the certificate {Thumbprint}", plan.Certificate?.Thumbprint);
                break;
            default:
                logger.LogWarning(
                    "The key ring is not encrypted at rest; it is protected only by the permissions of its folder. " +
                    "Set DataProtection:CertificatePath to encrypt it, or DataProtection:RequireEncryptedKeys to refuse " +
                    "to start without encryption");
                break;
        }
#pragma warning restore CA1848
    }

    /// <summary>
    ///     Finds the key files of the ring that hold their key unencrypted.
    /// </summary>
    /// <param name="folder">The folder of the key ring.</param>
    /// <returns>The names of the files; empty when there are none or the folder does not exist.</returns>
    /// <remarks>
    ///     An unencrypted key file holds a <c>masterKey</c> element; an encrypted one hides it inside an
    ///     <c>encryptedSecret</c> element. The text of a key is never returned. A file that cannot be read at this
    ///     moment (Data Protection is writing it) is skipped.
    /// </remarks>
    internal static List<string> FindPlaintextKeyFiles(string folder)
    {
        var found = new List<string>();
        if (!Directory.Exists(folder))
        {
            return found;
        }

        foreach (var file in Directory.EnumerateFiles(folder, KeyFilePattern))
        {
            try
            {
                if (File.ReadAllText(file).Contains(PlaintextMarker, StringComparison.Ordinal))
                {
                    found.Add(Path.GetFileName(file));
                }
            }
            catch (IOException)
            {
                // Being written right now: it is checked at the next start
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>
    ///     Tells whether the descriptor of a key can be read, which needs the key to be decrypted.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns><c>true</c> when the key can be decrypted.</returns>
    private static bool CanDecrypt(IKey key)
    {
        try
        {
            _ = key.Descriptor;
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidOperationException or ArgumentException
                                       or FormatException or XmlException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Creates the Windows DPAPI encryptor.
    /// </summary>
    /// <param name="localMachine"><c>true</c> to bind to the machine, <c>false</c> to bind to the current user.</param>
    /// <param name="loggerFactory">The logger factory of Data Protection.</param>
    /// <returns>The encryptor; creating it does not call DPAPI.</returns>
    /// <exception cref="PlatformNotSupportedException">Thrown off Windows, which the start-up check has refused earlier.</exception>
    private static IXmlEncryptor CreateDpapiEncryptor(bool localMachine, ILoggerFactory loggerFactory)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows DPAPI is only available on Windows.");
        }

        return new DpapiXmlEncryptor(localMachine, loggerFactory);
    }

    /// <summary>
    ///     Makes a plan that stops the start.
    /// </summary>
    /// <param name="message">The reason; it is put behind the common prefix.</param>
    /// <returns>The plan, without certificates.</returns>
    private static KeyRingPlan Rejected(string message) =>
        new(KeyRingProtectionMode.PermissionsOnly, false, false, null, [], InvalidPrefix + message);

    /// <summary>
    ///     Loads an earlier certificate, with the password of the current one or, failing that, without a password.
    /// </summary>
    /// <param name="path">The path of the certificate file.</param>
    /// <param name="password">The password of the current certificate, if there is one.</param>
    /// <param name="index">The position in the list, for the message.</param>
    /// <returns>The certificate with its private key.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the file cannot be used; names the setting and the path.</exception>
    /// <remarks>
    ///     An earlier certificate shares the password of the current one or has none; a separate password is not
    ///     supported, so a certificate with another password must be exported again.
    /// </remarks>
    private static X509Certificate2 LoadPrevious(string path, string? password, int index)
    {
        var setting = $"DataProtection:PreviousCertificatePaths:{index}";
        try
        {
            return LoadCertificate(path, null, password, setting);
        }
        catch (InvalidOperationException) when (password is not null)
        {
            // The password is not for this file: an earlier certificate may have none
            return LoadCertificate(path, null, null, setting);
        }
    }

    /// <summary>
    ///     Loads a certificate with its private key from a file.
    /// </summary>
    /// <param name="path">The path of the certificate file: PKCS #12 or PEM.</param>
    /// <param name="keyPath">The path of a separate PEM private key; <c>null</c> or white space for none.</param>
    /// <param name="password">The password of the file or of the encrypted PEM key; <c>null</c> for none.</param>
    /// <param name="setting">The setting that names the file, for messages.</param>
    /// <param name="loadPkcs12">
    ///     The loader of a PKCS #12 file, given the path, the password, and the key storage flags; <c>null</c> for the
    ///     framework loader. A seam for tests that need a platform that cannot load the file.
    /// </param>
    /// <returns>The certificate with an RSA private key. The caller owns it.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the file does not exist, cannot be read, is not a certificate, has a different password, has
    ///     no private key, or is not an RSA certificate, or when the platform cannot load it. The message names the
    ///     setting and the path, never the password.
    /// </exception>
    /// <remarks>
    ///     The format is told from the content: a file with a PEM header is PEM, anything else is PKCS #12. The key is
    ///     held in memory only (ephemeral) where the platform allows it; macOS does not support an ephemeral key set,
    ///     so the default key set is used there. The file is never changed. A certificate store is not used.
    /// </remarks>
    internal static X509Certificate2 LoadCertificate(
        string path,
        string? keyPath,
        string? password,
        string setting,
        Func<string, string?, X509KeyStorageFlags, X509Certificate2>? loadPkcs12 = null)
    {
        loadPkcs12 ??= (file, filePassword, storage) => X509CertificateLoader.LoadPkcs12FromFile(file, filePassword, storage);

        // Read the file once: it decides the format, and a missing or unreadable file is told apart from a bad one
        byte[] content;
        try
        {
            content = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidOperationException(Failure(setting, path, "the file does not exist."), ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException)
        {
            throw new InvalidOperationException(Failure(setting, path, "the file cannot be read."), ex);
        }

        var hasKeyFile = !string.IsNullOrWhiteSpace(keyPath);
        if (hasKeyFile && !File.Exists(keyPath))
        {
            throw new InvalidOperationException(
                Failure("DataProtection:CertificateKeyPath", keyPath!, "the file does not exist."));
        }

        X509Certificate2 certificate;
        try
        {
            var isPem = content.AsSpan().IndexOf("-----BEGIN"u8) >= 0;
            var separateKey = hasKeyFile ? keyPath : null;
            var flags = OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
            certificate = isPem
                ? LoadPem(path, separateKey, password)
                : loadPkcs12(path, password, flags);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException or IOException
                                       or NotSupportedException)
        {
            // NotSupportedException includes PlatformNotSupportedException: whatever the platform cannot load is the
            // operator's setting to change, never a crash
            throw new InvalidOperationException(
                Failure(setting, path, "wrong password or not a certificate with a private key. If the file is " +
                    "protected, set DataProtection:CertificatePassword or DataProtection:CertificatePasswordFile."),
                ex);
        }

        // Only an RSA key can unwrap what the key ring encrypts
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                Failure(setting, path, "wrong password or not a certificate with a private key."));
        }

        using var key = certificate.GetRSAPrivateKey();
        if (key is null)
        {
            certificate.Dispose();
            throw new InvalidOperationException(Failure(setting, path, "it is not a certificate with an RSA private key."));
        }

        return certificate;
    }

    /// <summary>
    ///     Loads a certificate and its key from PEM files.
    /// </summary>
    /// <param name="path">The PEM file with the certificate (and the key, when there is no separate key file).</param>
    /// <param name="keyPath">The PEM file with the private key, or <c>null</c> when it is in <paramref name="path"/>.</param>
    /// <param name="password">The password of an encrypted key; <c>null</c> for an unencrypted key.</param>
    /// <returns>The certificate with its private key.</returns>
    /// <exception cref="CryptographicException">Thrown when the PEM data is not usable.</exception>
    /// <remarks>
    ///     A password is tried first; a key that is not encrypted is read without it, so a password that is set for
    ///     the current certificate file does not break a PEM file with a plain key.
    /// </remarks>
    private static X509Certificate2 LoadPem(string path, string? keyPath, string? password)
    {
        if (password is not null)
        {
            try
            {
                return X509Certificate2.CreateFromEncryptedPemFile(path, password, keyPath);
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                // The key may not be encrypted: read it plain; if it is not, that read fails with the same kind of error
            }
        }

        return X509Certificate2.CreateFromPemFile(path, keyPath);
    }

    /// <summary>
    ///     Makes the message for a certificate that cannot be loaded.
    /// </summary>
    /// <param name="setting">The setting that names the file.</param>
    /// <param name="path">The path of the file; operator configuration, not a secret.</param>
    /// <param name="reason">Why it cannot be loaded.</param>
    /// <returns>The message.</returns>
    private static string Failure(string setting, string path, string reason) =>
        $"{setting} '{path}' could not be loaded: {reason}";

    /// <summary>
    ///     Reads a setting that is one of the names of an enumeration, in any case.
    /// </summary>
    /// <typeparam name="T">The enumeration.</typeparam>
    /// <param name="text">The setting; <c>null</c> and white space mean <paramref name="fallback"/>.</param>
    /// <param name="fallback">The value for a setting that is not set.</param>
    /// <param name="setting">The name of the setting, for the message.</param>
    /// <param name="choices">The accepted names, for the message.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the text is not the name of a value.</exception>
    /// <remarks>
    ///     Numbers are not accepted: only the names are part of the contract.
    /// </remarks>
    private static T ParseChoice<T>(string? text, T fallback, string setting, string choices)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var trimmed = text.Trim();
        var match = Enum.GetNames<T>().FirstOrDefault(name => string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new InvalidOperationException($"{setting} '{trimmed}' is not supported. Use {choices}.");
        }

        return Enum.Parse<T>(match);
    }

    /// <summary>
    ///     The values of <c>DataProtection:DpapiScope</c>.
    /// </summary>
    private enum DpapiScopeChoice
    {
        /// <summary>
        ///     The key files can be read only by the account of the server.
        /// </summary>
        CurrentUser,

        /// <summary>
        ///     The key files can be read by every account of the machine.
        /// </summary>
        LocalMachine
    }
}

/// <summary>
///     A key repository that restricts each key file to its owner as soon as Data Protection has stored it, on a
///     platform with Unix permissions.
/// </summary>
/// <remarks>
///     Data Protection creates a key file with the default permissions of the process, and rolls to a new key every
///     90 days while the server runs. <see cref="KeyRingProtection.RestrictKeyFiles(string)"/> restricts the files that
///     exist at start-up; this decorator does the same for those that are created later, so that no key file keeps
///     the default mode until the next start. The file that the inner repository wrote is not known by name (the
///     name comes from the friendly name, which may be empty), so the pass covers the XML files of the folder. The
///     folder itself is restricted to its owner, which keeps others out for the moment between the creation of the
///     file and the change of its mode. Nothing is done on Windows, where <see cref="KeyRingProtection.RestrictKeyFolder"/>
///     sets the access rules of the folder. A failure to set the mode is not swallowed: it surfaces from the call that
///     stored the key, as an <see cref="InvalidOperationException"/> that names <c>DataProtection:KeysPath</c>. The
///     class holds no state of its own and is as thread-safe as the repository that it decorates.
/// </remarks>
internal sealed class KeyFileRestrictingXmlRepository : IXmlRepository
{
    /// <summary>
    ///     The repository that reads and writes the key files.
    /// </summary>
    private readonly IXmlRepository _inner;

    /// <summary>
    ///     The folder of the key files.
    /// </summary>
    private readonly string _folder;

    /// <summary>
    ///     The operating system.
    /// </summary>
    private readonly KeyProtectionPlatform _platform;

    /// <summary>
    ///     The call that sets the mode of a file.
    /// </summary>
    private readonly Action<string, UnixFileMode> _setMode;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KeyFileRestrictingXmlRepository"/> class for the platform the
    ///     process runs on.
    /// </summary>
    /// <param name="inner">The repository to decorate. Must not be null.</param>
    /// <param name="folder">The folder that the repository writes to. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public KeyFileRestrictingXmlRepository(IXmlRepository inner, string folder)
        : this(inner, folder, KeyProtectionPlatform.Current, KeyRingProtection.SetUnixFileMode)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="KeyFileRestrictingXmlRepository"/> class.
    /// </summary>
    /// <param name="inner">The repository to decorate. Must not be null.</param>
    /// <param name="folder">The folder that the repository writes to. Must not be null.</param>
    /// <param name="platform">The platform; the files are only restricted when it has Unix permissions.</param>
    /// <param name="setMode">The call that sets the mode of a file; a test supplies a recording one. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when a reference argument is null.</exception>
    internal KeyFileRestrictingXmlRepository(
        IXmlRepository inner,
        string folder,
        KeyProtectionPlatform platform,
        Action<string, UnixFileMode> setMode)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(setMode);
        _inner = inner;
        _folder = folder;
        _platform = platform;
        _setMode = setMode;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<XElement> GetAllElements() => _inner.GetAllElements();

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the mode of a key file cannot be set; the message names the folder and
    ///     <c>DataProtection:KeysPath</c>.
    /// </exception>
    public void StoreElement(XElement element, string friendlyName)
    {
        // Let the repository write the file, then restrict what is in the folder, which includes the new file
        _inner.StoreElement(element, friendlyName);
        KeyRingProtection.RestrictKeyFiles(_folder, _platform, _setMode);
    }
}
