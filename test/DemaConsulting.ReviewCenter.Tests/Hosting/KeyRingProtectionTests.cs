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
using System.Xml.Linq;
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Tests.Hosting;

/// <summary>
///     Unit tests for the KeyRingProtection unit: the decision, the plan, the wiring, the check of the ring, and real
///     Data Protection round trips over temporary key folders with certificates that are created in the test. Nothing
///     uses a certificate store, the real home directory or (except for one Windows test) Windows DPAPI.
/// </summary>
public class KeyRingProtectionTests
{
    /// <summary>
    ///     A password that must never appear in a message.
    /// </summary>
    private const string Password = "Pa55-word-for-tests";

    /// <summary>
    ///     A Windows machine, whatever the machine of the test.
    /// </summary>
    private static readonly KeyProtectionPlatform WindowsPlatform = new(true, false);

    /// <summary>
    ///     A Linux machine, whatever the machine of the test.
    /// </summary>
    private static readonly KeyProtectionPlatform UnixPlatform = new(false, true);

    /// <summary>
    ///     An encryptor that records its scope and refuses to encrypt: a test that gets one proves that DPAPI was
    ///     chosen without calling DPAPI.
    /// </summary>
    /// <param name="localMachine">The scope it was created for.</param>
    private sealed class RecordingEncryptor(bool localMachine) : IXmlEncryptor
    {
        /// <summary>
        ///     Gets a value indicating whether it was created for the machine rather than the current user.
        /// </summary>
        public bool LocalMachine { get; } = localMachine;

        /// <inheritdoc />
        public EncryptedXmlInfo Encrypt(XElement plaintextElement) =>
            throw new NotSupportedException("A test must not encrypt with the recording encryptor.");
    }

    /// <summary>
    ///     A key ring over a folder, wired like the server wires it.
    /// </summary>
    private sealed class Ring : IDisposable
    {
        /// <summary>
        ///     The services of the ring.
        /// </summary>
        private readonly ServiceProvider _provider;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Ring"/> class.
        /// </summary>
        /// <param name="folder">The key folder.</param>
        /// <param name="settings">The settings.</param>
        /// <param name="platform">The platform to decide for.</param>
        public Ring(string folder, KeyRingSettings settings, KeyProtectionPlatform platform)
        {
            Plan = KeyRingProtection.Prepare(settings, platform);
            var services = new ServiceCollection();
            services.AddLogging();
            var builder = services.AddDataProtection()
                .SetApplicationName("KeyRingProtectionTests")
                .PersistKeysToFileSystem(new DirectoryInfo(folder));
            KeyRingProtection.Apply(builder, Plan);
            _provider = services.BuildServiceProvider();
        }

        /// <summary>
        ///     Gets the plan of the ring.
        /// </summary>
        public KeyRingPlan Plan { get; }

        /// <summary>
        ///     Gets the key manager of the ring.
        /// </summary>
        public IKeyManager Keys => _provider.GetRequiredService<IKeyManager>();

        /// <summary>
        ///     Encrypts a value with the ring, which creates the first key when there is none.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The encrypted value.</returns>
        public string Protect(string value) =>
            _provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect(value);

        /// <summary>
        ///     Decrypts a value that was encrypted with the ring.
        /// </summary>
        /// <param name="cipher">The encrypted value.</param>
        /// <returns>The value.</returns>
        public string Unprotect(string cipher) =>
            _provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(cipher);

        /// <inheritdoc />
        public void Dispose()
        {
            _provider.Dispose();
            Plan.Dispose();
        }
    }

    /// <summary>
    ///     Makes the settings of a certificate ring.
    /// </summary>
    /// <param name="path">The certificate file.</param>
    /// <param name="password">The password of the file, or <c>null</c>.</param>
    /// <param name="previous">The earlier certificate files.</param>
    /// <returns>The settings.</returns>
    private static KeyRingSettings CertificateSettings(string path, string? password = Password, params string[] previous) =>
        new()
        {
            Protection = "Certificate",
            CertificatePath = path,
            CertificatePassword = password,
            PreviousCertificatePaths = [.. previous]
        };

    /// <summary>
    ///     Lists the key files of a folder.
    /// </summary>
    /// <param name="folder">The key folder.</param>
    /// <returns>The paths of the key files.</returns>
    private static string[] KeyFiles(string folder) => Directory.GetFiles(folder, "key-*.xml");

    /// <summary>
    ///     Test that Auto chooses DPAPI on Windows when no certificate is configured.
    /// </summary>
    [Fact]
    public void KeyRingProtection_ResolveMode_AutoOnWindowsWithoutCertificate_ChoosesDpapi()
    {
        // Arrange: default settings
        var settings = new KeyRingSettings();

        // Act
        var mode = KeyRingProtection.ResolveMode(settings, WindowsPlatform);

        // Assert
        Assert.Equal(KeyRingProtectionMode.Dpapi, mode);
    }

    /// <summary>
    ///     Test that Auto chooses owner-only permissions on Unix when no certificate is configured.
    /// </summary>
    [Fact]
    public void KeyRingProtection_ResolveMode_AutoOnUnixWithoutCertificate_ChoosesPermissionsOnly()
    {
        // Arrange: default settings
        var settings = new KeyRingSettings { Protection = "auto" };

        // Act
        var mode = KeyRingProtection.ResolveMode(settings, UnixPlatform);

        // Assert
        Assert.Equal(KeyRingProtectionMode.PermissionsOnly, mode);
    }

    /// <summary>
    ///     Test that Auto chooses the certificate on every platform when a certificate path is configured.
    /// </summary>
    /// <param name="windows">Whether the platform is Windows.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KeyRingProtection_ResolveMode_AutoWithCertificatePath_ChoosesCertificate(bool windows)
    {
        // Arrange
        var settings = new KeyRingSettings { CertificatePath = "ring.pfx" };

        // Act
        var mode = KeyRingProtection.ResolveMode(settings, new KeyProtectionPlatform(windows, !windows));

        // Assert
        Assert.Equal(KeyRingProtectionMode.Certificate, mode);
    }

    /// <summary>
    ///     Test that an explicit mode is honored in any case and on any platform where it is possible.
    /// </summary>
    /// <param name="text">The setting.</param>
    /// <param name="expected">The name of the mode.</param>
    [Theory]
    [InlineData("Dpapi", "Dpapi")]
    [InlineData("dpapi", "Dpapi")]
    [InlineData("CERTIFICATE", "Certificate")]
    [InlineData("permissionsonly", "PermissionsOnly")]
    [InlineData(" PermissionsOnly ", "PermissionsOnly")]
    public void KeyRingProtection_ResolveMode_ExplicitChoice_IsHonoredInAnyCase(string text, string expected)
    {
        // Arrange: a certificate path, so that every choice is possible, on Windows, where every choice is possible
        var settings = new KeyRingSettings { Protection = text, CertificatePath = "ring.pfx" };

        // Act
        var mode = KeyRingProtection.ResolveMode(settings, WindowsPlatform);

        // Assert
        Assert.Equal(expected, mode.ToString());
    }

    /// <summary>
    ///     Test that DPAPI off Windows is refused with a message that names the setting.
    /// </summary>
    [Fact]
    public void KeyRingProtection_ResolveMode_DpapiOnUnix_ThrowsNamingTheSetting()
    {
        // Arrange
        var settings = new KeyRingSettings { Protection = "Dpapi" };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => KeyRingProtection.ResolveMode(settings, UnixPlatform));

        // Assert
        Assert.Equal("DataProtection:Protection Dpapi is only available on Windows.", exception.Message);
    }

    /// <summary>
    ///     Test that the certificate mode without a certificate path is refused with a message that names both
    ///     settings.
    /// </summary>
    [Fact]
    public void KeyRingProtection_ResolveMode_CertificateWithoutPath_ThrowsNamingTheSettings()
    {
        // Arrange
        var settings = new KeyRingSettings { Protection = "Certificate" };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => KeyRingProtection.ResolveMode(settings, UnixPlatform));

        // Assert
        Assert.Contains("DataProtection:CertificatePath", exception.Message, StringComparison.Ordinal);
        Assert.Contains("DataProtection:Protection", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an unknown mode, and a number, are refused with a message that lists the choices.
    /// </summary>
    /// <param name="text">The setting.</param>
    [Theory]
    [InlineData("Magic")]
    [InlineData("1")]
    public void KeyRingProtection_ResolveMode_UnknownProtection_ThrowsListingTheChoices(string text)
    {
        // Arrange
        var settings = new KeyRingSettings { Protection = text };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => KeyRingProtection.ResolveMode(settings, UnixPlatform));

        // Assert
        Assert.Equal(
            $"DataProtection:Protection '{text}' is not supported. Use Auto, Dpapi, Certificate or PermissionsOnly.",
            exception.Message);
    }

    /// <summary>
    ///     Test that a refused setting is reported as a problem of the plan with the common prefix, and that nothing
    ///     is thrown or loaded.
    /// </summary>
    [Fact]
    public void KeyRingProtection_Prepare_UnknownProtection_ReturnsProblemWithPrefix()
    {
        // Arrange
        var settings = new KeyRingSettings { Protection = "Magic" };

        // Act
        using var plan = KeyRingProtection.Prepare(settings, UnixPlatform);

        // Assert
        Assert.Equal(
            "Invalid key ring settings: DataProtection:Protection 'Magic' is not supported. Use Auto, Dpapi, Certificate or PermissionsOnly.",
            plan.Problem);
        Assert.Null(plan.Certificate);
    }

    /// <summary>
    ///     Test that an unknown DPAPI scope is refused with a message that lists the choices, and that a valid scope
    ///     is carried by the plan.
    /// </summary>
    [Fact]
    public void KeyRingProtection_Prepare_DpapiScope_IsCheckedAndCarried()
    {
        // Arrange: a Windows machine, so that no certificate and no DPAPI is needed
        var wrong = new KeyRingSettings { DpapiScope = "Everyone" };
        var machine = new KeyRingSettings { Protection = "Dpapi", DpapiScope = "localmachine" };
        var user = new KeyRingSettings { Protection = "Dpapi" };

        // Act
        using var wrongPlan = KeyRingProtection.Prepare(wrong, WindowsPlatform);
        using var machinePlan = KeyRingProtection.Prepare(machine, WindowsPlatform);
        using var userPlan = KeyRingProtection.Prepare(user, WindowsPlatform);

        // Assert
        Assert.Contains("DataProtection:DpapiScope 'Everyone' is not supported. Use CurrentUser or LocalMachine.", wrongPlan.Problem, StringComparison.Ordinal);
        Assert.Null(machinePlan.Problem);
        Assert.True(machinePlan.LocalMachine);
        Assert.False(userPlan.LocalMachine);
        Assert.Equal(KeyRingProtectionMode.Dpapi, userPlan.Mode);
    }

    /// <summary>
    ///     Test that a ring that is only protected by permissions is refused when encryption is required.
    /// </summary>
    [Fact]
    public void KeyRingProtection_Prepare_PermissionsOnlyWithRequireEncryptedKeys_ReturnsProblem()
    {
        // Arrange
        var settings = new KeyRingSettings { Protection = "PermissionsOnly", RequireEncryptedKeys = true };

        // Act
        using var plan = KeyRingProtection.Prepare(settings, UnixPlatform);

        // Assert
        Assert.Contains(
            "The key ring is not encrypted at rest and DataProtection:RequireEncryptedKeys is true.",
            plan.Problem,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that Auto on Unix without a certificate is refused when encryption is required.
    /// </summary>
    [Fact]
    public void KeyRingProtection_Prepare_AutoOnUnixWithRequireEncryptedKeys_ReturnsProblem()
    {
        // Arrange
        var settings = new KeyRingSettings { RequireEncryptedKeys = true };

        // Act
        using var plan = KeyRingProtection.Prepare(settings, UnixPlatform);

        // Assert
        Assert.Contains("DataProtection:RequireEncryptedKeys", plan.Problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a missing certificate file is reported with the setting and the path.
    /// </summary>
    [Fact]
    public void KeyRingProtection_Prepare_MissingCertificateFile_ReturnsProblemNamingTheSetting()
    {
        // Arrange
        using var folder = new TemporaryDirectory();
        var path = folder.GetFilePath("missing.pfx");

        // Act
        using var plan = KeyRingProtection.Prepare(CertificateSettings(path), UnixPlatform);

        // Assert
        Assert.Contains($"DataProtection:CertificatePath '{path}' could not be loaded", plan.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, plan.Problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a wrong password is reported with the setting and that the message does not hold either password.
    /// </summary>
    [Fact]
    public void KeyRingProtection_Prepare_WrongPassword_ReturnsProblemWithoutThePassword()
    {
        // Arrange: a PFX with one password and settings with another
        using var folder = new TemporaryDirectory();
        var path = KeyRingTestCertificates.WritePfx(folder, "ring", Password);

        // Act
        using var plan = KeyRingProtection.Prepare(CertificateSettings(path, "wrong-" + Password), UnixPlatform);

        // Assert
        Assert.Contains(
            $"DataProtection:CertificatePath '{path}' could not be loaded: wrong password or not a certificate with a private key.",
            plan.Problem,
            StringComparison.Ordinal);
        Assert.DoesNotContain(Password, plan.Problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a file that is not a certificate with a private key, and a certificate without a private key,
    ///     are refused.
    /// </summary>
    [Fact]
    public void KeyRingProtection_Prepare_NotACertificateWithPrivateKey_ReturnsProblem()
    {
        // Arrange: a text file and a public certificate
        using var folder = new TemporaryDirectory();
        var text = folder.GetFilePath("text.pfx");
        File.WriteAllText(text, "this is not a certificate");
        var publicOnly = KeyRingTestCertificates.WritePublicOnly(folder, "public");

        // Act
        using var textPlan = KeyRingProtection.Prepare(CertificateSettings(text, null), UnixPlatform);
        using var publicPlan = KeyRingProtection.Prepare(CertificateSettings(publicOnly, null), UnixPlatform);

        // Assert
        Assert.Contains("wrong password or not a certificate with a private key", textPlan.Problem, StringComparison.Ordinal);
        Assert.Contains("wrong password or not a certificate with a private key", publicPlan.Problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a certificate with a key that is not an RSA key is refused.
    /// </summary>
    [Fact]
    public void KeyRingProtection_Prepare_EcdsaCertificate_ReturnsProblemNamingRsa()
    {
        // Arrange
        using var folder = new TemporaryDirectory();
        var path = KeyRingTestCertificates.WriteEcdsaPfx(folder, "ec", Password);

        // Act
        using var plan = KeyRingProtection.Prepare(CertificateSettings(path), UnixPlatform);

        // Assert
        Assert.Contains("RSA", plan.Problem, StringComparison.Ordinal);
        Assert.Contains("DataProtection:CertificatePath", plan.Problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a platform that cannot load a PFX (for example one that does not support the key storage flags)
    ///     gives the message that names the setting, not an unhandled exception.
    /// </summary>
    [Fact]
    public void KeyRingProtection_LoadCertificate_PlatformCannotLoadPfx_ThrowsNamingTheSetting()
    {
        // Arrange: a real PFX file and a loader that behaves like a platform that refuses the key storage flags
        using var folder = new TemporaryDirectory();
        var path = KeyRingTestCertificates.WritePfx(folder, "ring", Password);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => KeyRingProtection.LoadCertificate(
            path,
            null,
            Password,
            "DataProtection:CertificatePath",
            (_, _, _) => throw new PlatformNotSupportedException("The key storage flags are not supported.")));

        // Assert: the setting and the path are named, the password is not, and the cause is kept
        Assert.Contains($"DataProtection:CertificatePath '{path}' could not be loaded", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
        Assert.IsType<PlatformNotSupportedException>(exception.InnerException);
    }

    /// <summary>
    ///     Test that the password is refused when it is given inline and in a file, without showing it.
    /// </summary>
    [Fact]
    public void KeyRingProtection_Prepare_PasswordAndPasswordFile_ReturnsProblemWithoutThePassword()
    {
        // Arrange
        using var folder = new TemporaryDirectory();
        var path = KeyRingTestCertificates.WritePfx(folder, "ring", Password);
        var passwordFile = folder.GetFilePath("password.txt");
        File.WriteAllText(passwordFile, Password);
        var settings = CertificateSettings(path);
        settings.CertificatePasswordFile = passwordFile;

        // Act
        using var plan = KeyRingProtection.Prepare(settings, UnixPlatform);

        // Assert
        Assert.Contains("DataProtection:CertificatePassword", plan.Problem, StringComparison.Ordinal);
        Assert.Contains("DataProtection:CertificatePasswordFile", plan.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, plan.Problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that every supported file format loads: PFX, PEM with the key inside, PEM with a separate key, and PEM
    ///     with an encrypted key.
    /// </summary>
    [Fact]
    public void KeyRingProtection_Prepare_SupportedFormats_LoadWithPrivateKey()
    {
        // Arrange: the four formats
        using var folder = new TemporaryDirectory();
        var pfx = KeyRingTestCertificates.WritePfx(folder, "pfx", Password);
        var (inside, _) = KeyRingTestCertificates.WritePem(folder, "inside", null, separateKey: false);
        var (separate, separateKey) = KeyRingTestCertificates.WritePem(folder, "separate", null, separateKey: true);
        var (encrypted, _) = KeyRingTestCertificates.WritePem(folder, "encrypted", Password, separateKey: false);
        var plans = new List<KeyRingPlan>
        {
            // The PFX has a password
            KeyRingProtection.Prepare(CertificateSettings(pfx), UnixPlatform),

            // The PEM files do not, or have a key that the password opens
            KeyRingProtection.Prepare(CertificateSettings(inside, null), UnixPlatform),
            KeyRingProtection.Prepare(
                new KeyRingSettings { Protection = "Certificate", CertificatePath = separate, CertificateKeyPath = separateKey },
                UnixPlatform),
            KeyRingProtection.Prepare(CertificateSettings(encrypted), UnixPlatform)
        };

        try
        {
            // Act and assert: no problem and a certificate with a private key each
            foreach (var plan in plans)
            {
                Assert.Null(plan.Problem);
                Assert.NotNull(plan.Certificate);
                Assert.True(plan.Certificate.HasPrivateKey);
            }
        }
        finally
        {
            plans.ForEach(plan => plan.Dispose());
        }
    }

    /// <summary>
    ///     Test that an earlier certificate loads with the password of the current one, or without a password, and
    ///     that an earlier certificate that cannot be loaded is reported with its position.
    /// </summary>
    [Fact]
    public void KeyRingProtection_Prepare_PreviousCertificates_LoadWithSharedPasswordOrNone()
    {
        // Arrange: the current one and two earlier ones, one with the shared password and one without any
        using var folder = new TemporaryDirectory();
        var current = KeyRingTestCertificates.WritePfx(folder, "current", Password);
        var shared = KeyRingTestCertificates.WritePfx(folder, "shared", Password);
        var plain = KeyRingTestCertificates.WritePfx(folder, "plain", null);
        var broken = folder.GetFilePath("broken.pfx");
        File.WriteAllText(broken, "broken");

        // Act
        using var plan = KeyRingProtection.Prepare(CertificateSettings(current, Password, shared, plain), UnixPlatform);
        using var brokenPlan = KeyRingProtection.Prepare(CertificateSettings(current, Password, shared, broken), UnixPlatform);

        // Assert
        Assert.Null(plan.Problem);
        Assert.Equal(2, plan.Previous.Count);
        Assert.Contains("DataProtection:PreviousCertificatePaths:1", brokenPlan.Problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the settings are read from the configuration section, case-insensitively, with the list of
    ///     earlier certificates, and that a value that cannot be bound is a problem of the plan.
    /// </summary>
    [Fact]
    public void KeyRingProtection_Prepare_Configuration_BindsTheSectionAndReportsBadValues()
    {
        // Arrange: a configuration with a valid section and one with a boolean that is not one
        using var folder = new TemporaryDirectory();
        var current = KeyRingTestCertificates.WritePfx(folder, "current", Password);
        var previous = KeyRingTestCertificates.WritePfx(folder, "previous", Password);
        var valid = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:protection"] = "certificate",
            ["DataProtection:CertificatePath"] = current,
            ["DataProtection:CertificatePassword"] = Password,
            ["DataProtection:PreviousCertificatePaths:0"] = previous,
            ["DataProtection:RequireEncryptedKeys"] = "true"
        }).Build();
        var invalid = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:RequireEncryptedKeys"] = "perhaps"
        }).Build();

        // Act
        using var plan = KeyRingProtection.Prepare(valid, UnixPlatform);
        using var invalidPlan = KeyRingProtection.Prepare(invalid, UnixPlatform);

        // Assert
        Assert.Null(plan.Problem);
        Assert.Equal(KeyRingProtectionMode.Certificate, plan.Mode);
        Assert.True(plan.RequireEncryptedKeys);
        Assert.Single(plan.Previous);
        Assert.StartsWith("Invalid key ring settings: ", invalidPlan.Problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the DPAPI choice reaches Data Protection as an encryptor made for the chosen scope, without
    ///     calling DPAPI.
    /// </summary>
    /// <param name="scope">The DPAPI scope setting.</param>
    /// <param name="expectedMachine">Whether the encryptor is for the machine.</param>
    [Theory]
    [InlineData("CurrentUser", false)]
    [InlineData("LocalMachine", true)]
    public void KeyRingProtection_Apply_Dpapi_CreatesTheEncryptorForTheScope(string scope, bool expectedMachine)
    {
        // Arrange: a Windows plan and a factory that records instead of calling DPAPI
        using var plan = KeyRingProtection.Prepare(
            new KeyRingSettings { Protection = "Dpapi", DpapiScope = scope },
            WindowsPlatform);
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddDataProtection();
        var created = new List<RecordingEncryptor>();

        // Act
        KeyRingProtection.Apply(builder, plan, (machine, _) =>
        {
            var encryptor = new RecordingEncryptor(machine);
            created.Add(encryptor);
            return encryptor;
        });
        using var provider = services.BuildServiceProvider();
        var chosen = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlEncryptor;

        // Assert: the encryptor of the options is the one the factory made for the scope
        var recording = Assert.IsType<RecordingEncryptor>(chosen);
        Assert.Same(created.Single(), recording);
        Assert.Equal(expectedMachine, recording.LocalMachine);
    }

    /// <summary>
    ///     Test that the certificate choice reaches Data Protection as the certificate encryptor, and that a plan
    ///     with a problem, or without encryption, wires no encryptor.
    /// </summary>
    [Fact]
    public void KeyRingProtection_Apply_CertificateAndOthers_WiresTheCertificateEncryptorOnly()
    {
        // Arrange: a certificate plan, a plan with a problem, and a permissions plan
        using var folder = new TemporaryDirectory();
        var path = KeyRingTestCertificates.WritePfx(folder, "ring", Password);
        using var certificatePlan = KeyRingProtection.Prepare(CertificateSettings(path), UnixPlatform);
        using var problemPlan = KeyRingProtection.Prepare(new KeyRingSettings { Protection = "Magic" }, UnixPlatform);
        using var permissionsPlan = KeyRingProtection.Prepare(new KeyRingSettings(), UnixPlatform);

        // Act
        var certificateEncryptor = EncryptorOf(certificatePlan);
        var problemEncryptor = EncryptorOf(problemPlan);
        var permissionsEncryptor = EncryptorOf(permissionsPlan);

        // Assert
        Assert.IsType<CertificateXmlEncryptor>(certificateEncryptor);
        Assert.Null(problemEncryptor);
        Assert.Null(permissionsEncryptor);

        static IXmlEncryptor? EncryptorOf(KeyRingPlan plan)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            KeyRingProtection.Apply(services.AddDataProtection(), plan);
            using var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlEncryptor;
        }
    }

    /// <summary>
    ///     Test that a certificate ring stores its keys encrypted, never in the clear, and that a restart with the
    ///     same certificate reads what was protected before.
    /// </summary>
    [Fact]
    public void KeyRingProtection_CertificateRing_KeyFileIsEncryptedAndRestartReadsIt()
    {
        // Arrange: a certificate and a ring that protects a value
        using var folder = new TemporaryDirectory();
        var keys = folder.GetFilePath("keys");
        Directory.CreateDirectory(keys);
        var path = KeyRingTestCertificates.WritePfx(folder, "ring", Password);
        string cipher;
        using (var first = new Ring(keys, CertificateSettings(path), UnixPlatform))
        {
            cipher = first.Protect("tkn-Certificate");
        }

        // Act: read the key file, and start again with the same certificate
        var file = Assert.Single(KeyFiles(keys));
        var text = File.ReadAllText(file);
        using var second = new Ring(keys, new KeyRingSettings { Protection = "Certificate", CertificatePath = path, CertificatePassword = Password, RequireEncryptedKeys = true }, UnixPlatform);
        var plain = second.Unprotect(cipher);

        // Assert: encrypted at rest, readable after the restart, and the check of the ring finds nothing to refuse
        Assert.Contains("<encryptedSecret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<masterKey", text, StringComparison.Ordinal);
        Assert.Equal("tkn-Certificate", plain);
        KeyRingProtection.CheckRing(second.Plan, second.Keys, keys, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

    /// <summary>
    ///     Test that the password can be read from a file, and that a ring that was protected with a certificate can
    ///     still be read after the certificate was replaced, when the earlier one is listed.
    /// </summary>
    [Fact]
    public void KeyRingProtection_CertificateRing_PreviousCertificateAndPasswordFile_StillReadTheRing()
    {
        // Arrange: a ring protected with the first certificate
        using var folder = new TemporaryDirectory();
        var keys = folder.GetFilePath("keys");
        Directory.CreateDirectory(keys);
        var first = KeyRingTestCertificates.WritePfx(folder, "first", Password);
        var second = KeyRingTestCertificates.WritePfx(folder, "second", Password);
        var passwordFile = folder.GetFilePath("password.txt");
        File.WriteAllText(passwordFile, Password + "\n");
        string cipher;
        using (var before = new Ring(keys, CertificateSettings(first), UnixPlatform))
        {
            cipher = before.Protect("tkn-Rotation");
        }

        // Act: start with the second certificate as the current one, the first as an earlier one, and the password
        // read from a file
        var rotated = new KeyRingSettings
        {
            Protection = "Certificate",
            CertificatePath = second,
            CertificatePasswordFile = passwordFile,
            PreviousCertificatePaths = [first]
        };
        using var after = new Ring(keys, rotated, UnixPlatform);
        KeyRingProtection.CheckRing(after.Plan, after.Keys, keys, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var plain = after.Unprotect(cipher);

        // Assert
        Assert.Null(after.Plan.Problem);
        Assert.Equal("tkn-Rotation", plain);
    }

    /// <summary>
    ///     Test that a ring that cannot be decrypted with the certificate of the start is refused, and that nothing
    ///     is written: Data Protection alone would have created a second key and lost every stored secret.
    /// </summary>
    [Fact]
    public void KeyRingProtection_CheckRing_WrongCertificate_RefusesWithoutCreatingASecondKey()
    {
        // Arrange: a ring protected with one certificate, and a start with another
        using var folder = new TemporaryDirectory();
        var keys = folder.GetFilePath("keys");
        Directory.CreateDirectory(keys);
        var right = KeyRingTestCertificates.WritePfx(folder, "right", Password);
        var wrong = KeyRingTestCertificates.WritePfx(folder, "wrong", Password);
        using (var before = new Ring(keys, CertificateSettings(right), UnixPlatform))
        {
            before.Protect("tkn-Guard");
        }

        var filesBefore = KeyFiles(keys);
        using var after = new Ring(keys, CertificateSettings(wrong), UnixPlatform);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => KeyRingProtection.CheckRing(
            after.Plan,
            after.Keys,
            keys,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));

        // Assert: the message names the settings and the check created no key
        Assert.Contains("DataProtection:CertificatePath", exception.Message, StringComparison.Ordinal);
        Assert.Contains("DataProtection:PreviousCertificatePaths", exception.Message, StringComparison.Ordinal);
        Assert.Equal(filesBefore, KeyFiles(keys));
    }

    /// <summary>
    ///     Test that an encrypted ring is refused when the start has no certificate at all.
    /// </summary>
    [Fact]
    public void KeyRingProtection_CheckRing_EncryptedRingWithoutCertificate_Refuses()
    {
        // Arrange: a certificate ring, and a start that only has owner permissions
        using var folder = new TemporaryDirectory();
        var keys = folder.GetFilePath("keys");
        Directory.CreateDirectory(keys);
        var path = KeyRingTestCertificates.WritePfx(folder, "ring", Password);
        using (var before = new Ring(keys, CertificateSettings(path), UnixPlatform))
        {
            before.Protect("tkn-NoCertificate");
        }

        using var after = new Ring(keys, new KeyRingSettings { Protection = "PermissionsOnly" }, UnixPlatform);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => KeyRingProtection.CheckRing(
            after.Plan,
            after.Keys,
            keys,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));

        // Assert
        Assert.Contains("DataProtection:CertificatePath", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an empty ring, and a folder that does not exist yet, are fine: they are new rings.
    /// </summary>
    [Fact]
    public void KeyRingProtection_CheckRing_EmptyRing_Passes()
    {
        // Arrange: an empty folder, a folder that does not exist yet, and a plan that requires encryption
        using var folder = new TemporaryDirectory();
        var keys = folder.GetFilePath("keys");
        Directory.CreateDirectory(keys);
        var notYet = folder.GetFilePath("not-yet");
        var path = KeyRingTestCertificates.WritePfx(folder, "ring", Password);
        var settings = CertificateSettings(path);
        settings.RequireEncryptedKeys = true;
        using var ring = new Ring(keys, settings, UnixPlatform);
        using var newFolderRing = new Ring(notYet, settings, UnixPlatform);

        // Act
        KeyRingProtection.CheckRing(ring.Plan, ring.Keys, keys, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        KeyRingProtection.CheckRing(newFolderRing.Plan, newFolderRing.Keys, notYet, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        // Assert: nothing was created in either ring
        Assert.Empty(KeyFiles(keys));
        Assert.Empty(Directory.Exists(notYet) ? KeyFiles(notYet) : []);
    }

    /// <summary>
    ///     Test that a key file with an unencrypted key is refused when encryption is required, and found otherwise.
    /// </summary>
    [Fact]
    public void KeyRingProtection_CheckRing_RequireEncryptedKeysWithPlaintextKeyFile_Refuses()
    {
        // Arrange: a plaintext key file in the ring, as a start without protection leaves it
        using var folder = new TemporaryDirectory();
        var keys = folder.GetFilePath("keys");
        Directory.CreateDirectory(keys);
        File.WriteAllText(Path.Combine(keys, "key-00000000-0000-0000-0000-000000000001.xml"), "<key><descriptor><masterKey /></descriptor></key>");
        var path = KeyRingTestCertificates.WritePfx(folder, "ring", Password);
        var settings = CertificateSettings(path);
        settings.RequireEncryptedKeys = true;
        using var ring = new Ring(keys, settings, UnixPlatform);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => KeyRingProtection.CheckRing(
            ring.Plan,
            ring.Keys,
            keys,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));

        // Assert: the message names the setting and the file, and the finder reports the file
        Assert.Contains("DataProtection:RequireEncryptedKeys", exception.Message, StringComparison.Ordinal);
        Assert.Contains("key-00000000-0000-0000-0000-000000000001.xml", exception.Message, StringComparison.Ordinal);
        Assert.Single(KeyRingProtection.FindPlaintextKeyFiles(keys));
    }

    /// <summary>
    ///     Test that older keys that cannot be decrypted do not stop the start, but are logged as an error that names
    ///     the setting to fix.
    /// </summary>
    [Fact]
    public void KeyRingProtection_CheckRing_OlderKeyUnreadable_LogsErrorAndPasses()
    {
        // Arrange: a ring with an old key of the first certificate and a newer key of the second, which the start has;
        // the keys get explicit dates so that which one is the newest does not depend on the clock
        using var folder = new TemporaryDirectory();
        var keys = folder.GetFilePath("keys");
        Directory.CreateDirectory(keys);
        var first = KeyRingTestCertificates.WritePfx(folder, "first", Password);
        var second = KeyRingTestCertificates.WritePfx(folder, "second", Password);
        var now = DateTimeOffset.UtcNow;
        using (var before = new Ring(keys, CertificateSettings(first), UnixPlatform))
        {
            before.Keys.CreateNewKey(now.AddDays(-10), now.AddDays(80));
        }

        using var after = new Ring(keys, CertificateSettings(second), UnixPlatform);
        after.Keys.CreateNewKey(now.AddDays(-1), now.AddDays(89));
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        // Act
        KeyRingProtection.CheckRing(after.Plan, after.Keys, keys, loggerFactory.CreateLogger("test"));

        // Assert: one error that names the remedy
        var entry = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("DataProtection:PreviousCertificatePaths", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that what protects the ring is logged: a warning for permissions only, information for the others.
    /// </summary>
    [Fact]
    public void KeyRingProtection_LogProtection_EachMode_LogsTheProtectionInForce()
    {
        // Arrange: a permissions plan, a DPAPI plan and a certificate plan
        using var folder = new TemporaryDirectory();
        var path = KeyRingTestCertificates.WritePfx(folder, "ring", Password);
        using var permissions = KeyRingProtection.Prepare(new KeyRingSettings(), UnixPlatform);
        using var dpapi = KeyRingProtection.Prepare(new KeyRingSettings { Protection = "Dpapi" }, WindowsPlatform);
        using var machine = KeyRingProtection.Prepare(
            new KeyRingSettings { Protection = "Dpapi", DpapiScope = "LocalMachine" },
            WindowsPlatform);
        using var certificate = KeyRingProtection.Prepare(CertificateSettings(path), UnixPlatform);
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var logger = loggerFactory.CreateLogger("test");

        // Act: the permissions plan twice, as two starts
        KeyRingProtection.LogProtection(permissions, logger);
        KeyRingProtection.LogProtection(permissions, logger);
        KeyRingProtection.LogProtection(dpapi, logger);
        KeyRingProtection.LogProtection(machine, logger);
        KeyRingProtection.LogProtection(certificate, logger);

        // Assert
        var entries = logs.Entries;
        Assert.Equal(5, entries.Count);
        Assert.All(entries.Take(2), entry =>
        {
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Contains("not encrypted at rest", entry.Message, StringComparison.Ordinal);
            Assert.Contains("DataProtection:CertificatePath", entry.Message, StringComparison.Ordinal);
            Assert.Contains("DataProtection:RequireEncryptedKeys", entry.Message, StringComparison.Ordinal);
        });
        Assert.Equal("Key ring protected with Windows DPAPI (CurrentUser)", entries[2].Message);
        Assert.Equal("Key ring protected with Windows DPAPI (LocalMachine)", entries[3].Message);
        Assert.Equal(LogLevel.Information, entries[4].Level);
        Assert.Contains(certificate.Certificate!.Thumbprint, entries[4].Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the key files are restricted to their owner on Unix, whatever they were created with.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void KeyRingProtection_RestrictKeyFiles_UnixKeyFiles_AreOwnerReadWriteOnly()
    {
        // Arrange: a key file that is readable by everybody
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file permissions are only enforced on Linux and macOS.");
        using var folder = new TemporaryDirectory();
        var file = folder.GetFilePath("key-00000000-0000-0000-0000-000000000001.xml");
        File.WriteAllText(file, "<key />");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        // Act
        KeyRingProtection.RestrictKeyFiles(folder.DirectoryPath);

        // Assert
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
    }

    /// <summary>
    ///     Test that Windows DPAPI protects a real key ring: the key file holds no clear key and a restart reads what
    ///     was protected before. This is the one test that calls DPAPI; it runs only on Windows, in a temporary
    ///     folder, with the account of the test.
    /// </summary>
    [Fact]
    public void KeyRingProtection_DpapiRing_WindowsKeyFileIsEncryptedAndRestartReadsIt()
    {
        // Arrange: a ring in DPAPI mode, as Auto chooses on Windows
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows DPAPI is only available on Windows.");
        using var folder = new TemporaryDirectory();
        var keys = folder.GetFilePath("keys");
        Directory.CreateDirectory(keys);
        var settings = new KeyRingSettings { Protection = "Dpapi" };
        string cipher;
        using (var first = new Ring(keys, settings, KeyProtectionPlatform.Current))
        {
            cipher = first.Protect("tkn-Dpapi");
        }

        // Act: read the key file and start again
        var text = File.ReadAllText(Assert.Single(KeyFiles(keys)));
        using var second = new Ring(keys, settings, KeyProtectionPlatform.Current);
        var plain = second.Unprotect(cipher);

        // Assert: encrypted with DPAPI, readable after the restart, and the check of the ring finds nothing wrong
        Assert.Contains("<encryptedSecret", text, StringComparison.Ordinal);
        Assert.Contains("DpapiXmlDecryptor", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<masterKey", text, StringComparison.Ordinal);
        Assert.Equal("tkn-Dpapi", plain);
        KeyRingProtection.CheckRing(second.Plan, second.Keys, keys, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }
}
