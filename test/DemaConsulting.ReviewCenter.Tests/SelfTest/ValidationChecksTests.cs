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

using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.SelfTest;
using DemaConsulting.ReviewCenter.SelfTest.Checks;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Tests.Hosting;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Tests.SelfTest;

/// <summary>
///     Unit tests for the individual self-validation checks. Each check is exercised once: the light ones directly,
///     and the ones that start git, a review workflow, a signer or a web host through the one shared real run of
///     the default validation (see <see cref="RealValidationRun"/>), so the suite does not pay for them twice.
/// </summary>
[Collection("Sequential")]
public partial class ValidationChecksTests
{
    /// <summary>
    ///     Matches the line of the host check that reports the loopback port it listened on.
    /// </summary>
    /// <returns>The pattern; group 1 is the port.</returns>
    [GeneratedRegex(@"ReviewCenter_HostStart - Passed \(\d+ ms\): listened on 127\.0\.0\.1:(\d+);")]
    private static partial Regex HostListeningLine();

    /// <summary>
    ///     The pinned fingerprint of the fingerprint check, computed once and independently of the production code.
    /// </summary>
    private const string PinnedFingerprint = "13e028da06299fcedffff47b2b55cca46a68111f3124d77d355364339c4215f2";

    /// <summary>
    ///     Test that the fingerprint check compares with the pinned literal value.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_Fingerprint_Run_MatchesPinnedLiteral()
    {
        // Arrange: nothing; the check hashes its own fixed files

        // Act: run the check
        var detail = await FingerprintCheck.RunAsync(CancellationToken.None);

        // Assert: the check passed and reports the pinned fingerprint that this test also pins
        Assert.Equal(PinnedFingerprint, FingerprintCheck.ExpectedFingerprint);
        Assert.Contains(PinnedFingerprint[..12], detail);
    }

    /// <summary>
    ///     Test that the password hash check passes.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_PasswordHash_Run_AcceptsRightAndRejectsWrongPassword()
    {
        // Arrange: nothing; the check hashes a throw-away password

        // Act: run the check, which throws when a right password is rejected or a wrong one is accepted
        var detail = await PasswordHashCheck.RunAsync(CancellationToken.None);

        // Assert: the check reports a result
        Assert.False(string.IsNullOrWhiteSpace(detail));
    }

    /// <summary>
    ///     Test that the TOTP check passes with the RFC 6238 vectors.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_Totp_Run_PassesWithRfcVectors()
    {
        // Arrange: nothing; the check computes codes for the published vectors

        // Act: run the check, which throws when any published code or window rule is violated
        var detail = await TotpCheck.RunAsync(CancellationToken.None);

        // Assert: the check reports a result and the name is the documented one
        Assert.Equal("ReviewCenter_Totp", TotpCheck.Name);
        Assert.Contains("RFC 6238", detail);
    }

    /// <summary>
    ///     Test that the audit chain check detects a tampered entry.
    /// </summary>
    [Fact]
    public void ValidationChecks_AuditChain_Run_DetectsTamperedEntry()
    {
        // Arrange: the shared real run, in which the check built its own chain in a temporary database

        // Act: read its log
        var log = RealValidationRun.Result.Log;

        // Assert: the check passed, which means the intact chain verified and the changed entry was reported
        Assert.Matches(@"✓ ReviewCenter_AuditChain - Passed \(\d+ ms\): 3 entries intact; change of entry 2 detected", log);
    }

    /// <summary>
    ///     Test that the checks that start git, the workflow, the signer and the web host each report in the real run.
    /// </summary>
    [Fact]
    public void ValidationChecks_RealRun_HeavyChecks_ReportTheirDetails()
    {
        // Arrange: the shared real run

        // Act: read its log
        var log = RealValidationRun.Result.Log;

        // Assert: each check shows what it verified
        Assert.Contains("ReviewCenter_GitAvailable - Passed", log);
        Assert.Contains("ReviewCenter_GitRoundTrip - Passed", log);
        Assert.Contains("embedded SQLite", log);
        Assert.Contains("ReviewCenter_AttestationPdf - Passed", log);
        Assert.Contains("/login", log);
        Assert.Contains("stopped cleanly", log);
    }

    /// <summary>
    ///     Test that the review workflow check shows that the author's checklist answer is refused, answers and signs
    ///     with a separate reviewer, also shows that the author's signature is refused, that the approved review is
    ///     locked and that an administrator's reopen unlocks it.
    /// </summary>
    [Fact]
    public void ReviewWorkflowCheck_RunAsync_SeparateReviewerLockAndReopen_ReportsDetails()
    {
        // Arrange: the shared real run, in which the workflow check ran on a temporary embedded database

        // Act: read its log
        var log = RealValidationRun.Result.Log;

        // Assert: the check passed and reports what it proved, in the order of the workflow
        Assert.Equal("ReviewCenter_ReviewWorkflow", ReviewWorkflowCheck.Name);
        Assert.Matches(
            @"ReviewCenter_ReviewWorkflow - Passed \(\d+ ms\): embedded SQLite; author answer refused; "
            + @"review answered and signed by a separate reviewer; "
            + @"author signature refused; approved review locked and reopened by an administrator; "
            + @"evidence index fingerprint \w+…, record digest \w+…",
            log);
    }

    /// <summary>
    ///     Test that the real validation passes in a hostile environment and uses nothing of it: the program copy has
    ///     a broken <c>appsettings.json</c> (and so does the working directory), and the environment names all-interface
    ///     listen addresses, the Development environment and a database, key ring, git cache and attestation key
    ///     outside the temporary folders. Before the host was built isolated, the broken file made the host check fail
    ///     and the ambient endpoint decided where it listened.
    /// </summary>
    [Fact]
    public void ValidationChecks_HostStart_HostileEnvironment_PassesOnLoopbackAndTouchesNothingAmbient()
    {
        // Arrange: the shared real process run, made in the hostile environment (see RealValidationRun)

        // Act: read the outcome of that run
        var run = RealValidationRun.Process;

        // Assert: every check passed, including the host check, which listened on one loopback port that is not the
        // poisoned one
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Passed: 9", run.Output);
        var match = HostListeningLine().Match(run.Output);
        Assert.True(match.Success, "The host check should report a loopback address:" + Environment.NewLine + run.Output);
        Assert.NotEqual(run.PoisonedPort, int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));

        // Assert: nothing was created where the poisoned settings pointed, and the checks removed all they created
        Assert.Empty(run.SentinelEntries);
        Assert.Empty(run.PrivateTempEntries);
    }

    /// <summary>
    ///     Test that the opt-in check is skipped, not failed, when nothing is configured.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredDatabase_NothingConfigured_IsSkipped()
    {
        // Arrange: no configured connection string and no database at the default location
        Assert.SkipWhen(
            File.Exists(DatabaseSetup.GetDefaultDatabasePath()),
            "A database exists at the default location of this machine.");
        using var configuration = new ConfiguredDatabaseEnvironment(null);

        // Act: run the check
        var exception = await Assert.ThrowsAsync<ValidationSkippedException>(
            () => ConfiguredDatabaseCheck.RunAsync(CancellationToken.None));

        // Assert: the reason says that nothing is configured
        Assert.Contains("No database is configured", exception.Message);
    }

    /// <summary>
    ///     Test that the opt-in check passes for a database with the current schema, and changes nothing.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredDatabase_CurrentSchema_PassesWithoutWriting()
    {
        // Arrange: a database with the current schema in a temporary folder
        using var folder = new TemporaryDirectory();
        var connectionString = CreateDatabase(folder);
        using var configuration = new ConfiguredDatabaseEnvironment(connectionString);
        var path = folder.GetFilePath("review.db");
        var before = (File.GetLastWriteTimeUtc(path), new FileInfo(path).Length);

        // Act: run the check
        var detail = await ConfiguredDatabaseCheck.RunAsync(CancellationToken.None);

        // Assert: the schema matches, the file is untouched and the report does not reveal the path
        Assert.Contains("schema matches", detail);
        Assert.Contains("opened read-only", detail);
        Assert.DoesNotContain(folder.DirectoryPath, detail);
        Assert.Equal(before, (File.GetLastWriteTimeUtc(path), new FileInfo(path).Length));
    }

    /// <summary>
    ///     Test that the opt-in check fails with the table name, not the path, for a database with a missing table.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredDatabase_MissingTable_FailsNamingTableOnly()
    {
        // Arrange: a database of the current schema from which one table was dropped
        using var folder = new TemporaryDirectory();
        var connectionString = CreateDatabase(folder);
        var table = DropFirstTable(connectionString);
        using var configuration = new ConfiguredDatabaseEnvironment(connectionString);

        // Act: run the check
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredDatabaseCheck.RunAsync(CancellationToken.None));

        // Assert: the message names the table and neither the folder nor the connection string
        Assert.Contains($"table '{table}' is missing", exception.Message);
        Assert.DoesNotContain(folder.DirectoryPath, exception.Message);
        Assert.DoesNotContain("Data Source", exception.Message);
    }

    /// <summary>
    ///     Test that the opt-in check is skipped for a configured database file that does not exist yet (a fresh
    ///     installation), without creating it.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredDatabase_MissingFile_IsSkippedWithoutCreatingIt()
    {
        // Arrange: a connection string to a file that does not exist
        using var folder = new TemporaryDirectory();
        var path = folder.GetFilePath("absent.db");
        using var configuration = new ConfiguredDatabaseEnvironment($"Data Source={path};Pooling=False");

        // Act: run the check
        var exception = await Assert.ThrowsAsync<ValidationSkippedException>(
            () => ConfiguredDatabaseCheck.RunAsync(CancellationToken.None));

        // Assert: it is skipped with the reason, does not leak the path and does not create the file
        Assert.Contains("does not exist yet", exception.Message);
        Assert.DoesNotContain(folder.DirectoryPath, exception.Message);
        Assert.False(File.Exists(path), "The check must never create the database");
    }

    /// <summary>
    ///     Test that the opt-in check fails for an unsupported provider.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredDatabase_UnsupportedProvider_Fails()
    {
        // Arrange: a configured provider that this version does not support
        using var configuration = new ConfiguredDatabaseEnvironment("Data Source=x.db", provider: "Oracle");

        // Act: run the check
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredDatabaseCheck.RunAsync(CancellationToken.None));

        // Assert: the provider is named
        Assert.Contains("Oracle", exception.Message);
    }

    /// <summary>
    ///     Test that the key check is skipped, not failed, when nothing is configured.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_NothingConfigured_IsSkipped()
    {
        // Arrange: no configured connection string and no database at the default location
        Assert.SkipWhen(
            File.Exists(DatabaseSetup.GetDefaultDatabasePath()),
            "A database exists at the default location of this machine.");
        using var configuration = new ConfiguredKeysEnvironment(new KeyValuePair<string, string?>(TestDatabase.ConnectionStringVariable, null));

        // Act: run the check
        var exception = await Assert.ThrowsAsync<ValidationSkippedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: the reason says that nothing is configured
        Assert.Contains("No database is configured", exception.Message);
    }

    /// <summary>
    ///     Test that the key check is skipped for a database file that does not exist yet, and creates nothing.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_MissingDatabase_IsSkippedWithoutCreatingAnything()
    {
        // Arrange: a database file and a key folder that do not exist yet
        using var folder = new TemporaryDirectory();
        var database = folder.GetFilePath("absent.db");
        var keys = Path.Combine(folder.DirectoryPath, "keys");
        using var configuration = new ConfiguredKeysEnvironment(
            new(TestDatabase.ConnectionStringVariable, $"Data Source={database};Pooling=False"),
            new("DataProtection__KeysPath", keys));

        // Act
        var exception = await Assert.ThrowsAsync<ValidationSkippedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: skipped with the reason, no path in the message, nothing created
        Assert.Contains("does not exist yet", exception.Message);
        Assert.DoesNotContain(folder.DirectoryPath, exception.Message);
        Assert.False(File.Exists(database), "The check must never create the database");
        Assert.False(Directory.Exists(keys), "The check must never create the key folder");
    }

    /// <summary>
    ///     Test that the key check is skipped for a database without secrets when the key folder does not exist yet
    ///     (a fresh installation), and does not create the folder.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_NoKeyFolderAndNoSecrets_IsSkippedWithoutCreatingTheFolder()
    {
        // Arrange: a database with the schema and no secrets, and a key folder that does not exist
        using var install = new KeysInstall();
        using var configuration = install.ConfigureKeysPath(Path.Combine(install.Folder.DirectoryPath, "not-yet"));

        // Act
        var exception = await Assert.ThrowsAsync<ValidationSkippedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("key folder does not exist yet", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(install.Folder.DirectoryPath, "not-yet")));
    }

    /// <summary>
    ///     Test that the key check passes when the ring reads every secret, reports the counts without the secrets and
    ///     without paths, and changes nothing.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_RingMatchesDatabase_PassesWithoutChangingAnything()
    {
        // Arrange: an installation with a repository token and a second-factor secret
        using var install = new KeysInstall();
        var cipher = install.AddToken(1, KeysInstall.TokenText);
        install.AddTotp(2);
        using var configuration = install.Configure();
        var before = install.Snapshot();

        // Act
        var detail = await ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken);

        // Assert: the counts, no secret, no ciphertext, no path, and no file was touched or created
        Assert.Contains("protection PermissionsOnly", detail);
        Assert.Contains("key files 1 (1 not encrypted)", detail);
        Assert.Contains("protected secrets 2 (repository tokens 1, second-factor secrets 1)", detail);
        Assert.Contains("unreadable 0", detail);
        Assert.DoesNotContain(KeysInstall.TokenText, detail);
        Assert.DoesNotContain(cipher, detail);
        Assert.DoesNotContain(install.Folder.DirectoryPath, detail);
        Assert.Contains("attestation key SelfSigned: will be created on first start", detail);
        Assert.False(Directory.Exists(install.AttestationFolder));
        Assert.Equal(before, install.Snapshot());
    }

    /// <summary>
    ///     Test that the key check describes an existing protected self-signed attestation key by subject,
    ///     thumbprint and validity, without a path, and changes nothing.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_AttestationKeyExists_ReportsSubjectThumbprintAndValidity()
    {
        // Arrange: an installation whose server has created the protected attestation key
        using var install = new KeysInstall();
        var thumbprint = install.CreateAttestationKey();
        install.AddToken(1, KeysInstall.TokenText);
        using var configuration = install.Configure();
        var before = install.Snapshot();

        // Act
        var detail = await ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken);

        // Assert: the public description of the key, no path, and nothing was changed
        Assert.Contains("attestation key SelfSigned: subject 'CN=ReviewCenter self-signed attestation key", detail);
        Assert.Contains($"thumbprint {thumbprint}", detail);
        Assert.Contains(", valid ", detail);
        Assert.DoesNotContain("will be created", detail);
        Assert.DoesNotContain(install.Folder.DirectoryPath, detail);
        Assert.Equal(before, install.Snapshot());
    }

    /// <summary>
    ///     Test that the key check reports an attestation key file of the earlier format, which is not encrypted, as
    ///     a warning and that the check still passes and creates no key.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_LegacyAttestationFile_ReportsWarningAndPasses()
    {
        // Arrange: a plaintext key file of the earlier format and no protected file
        using var install = new KeysInstall();
        install.AddToken(1, KeysInstall.TokenText);
        Directory.CreateDirectory(install.AttestationFolder);
        await File.WriteAllTextAsync(
            Path.Combine(install.AttestationFolder, SelfSignedKeyProvider.LegacyFileName),
            "legacy",
            TestContext.Current.CancellationToken);
        using var configuration = install.Configure();
        var before = install.Snapshot();

        // Act
        var detail = await ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken);

        // Assert: the warning says to delete the file, the key will be created, and nothing was written
        Assert.Contains("will be created on first start", detail);
        Assert.Contains($"warning: the file {SelfSignedKeyProvider.LegacyFileName} of an earlier version", detail);
        Assert.Contains("delete it", detail);
        Assert.DoesNotContain(install.Folder.DirectoryPath, detail);
        Assert.Equal(before, install.Snapshot());
    }

    /// <summary>
    ///     Test that the key check fails when the protected attestation key cannot be decrypted with the key ring,
    ///     naming the file and not the folder, and creates nothing.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_UnreadableAttestationKey_FailsNamingTheFile()
    {
        // Arrange: a protected key file that the ring cannot read
        using var install = new KeysInstall();
        install.AddToken(1, KeysInstall.TokenText);
        Directory.CreateDirectory(install.AttestationFolder);
        await File.WriteAllBytesAsync(
            Path.Combine(install.AttestationFolder, SelfSignedKeyProvider.FileName),
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
            TestContext.Current.CancellationToken);
        using var configuration = install.Configure();
        var before = install.Snapshot();

        // Act
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: the file and the remedy are named, the folder is not, and nothing was changed
        Assert.Contains($"attestation key file {SelfSignedKeyProvider.FileName} cannot be decrypted", exception.Message);
        Assert.Contains("delete the file", exception.Message);
        Assert.DoesNotContain(install.Folder.DirectoryPath, exception.Message);
        Assert.Equal(before, install.Snapshot());
    }

    /// <summary>
    ///     Test that the key check fails for an attestation key file that cannot be read because of an I/O error, and
    ///     that the message names the setting to correct but not the key folder.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_AttestationKeyIoError_FailsWithoutNamingTheFolder()
    {
        // Arrange: a protected key file that another process holds open exclusively
        using var install = new KeysInstall();
        install.AddToken(1, KeysInstall.TokenText);
        Directory.CreateDirectory(install.AttestationFolder);
        var path = Path.Combine(install.AttestationFolder, SelfSignedKeyProvider.FileName);
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10], TestContext.Current.CancellationToken);
        using var configuration = install.Configure();
        await using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Act
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: the setting is named, the folder is not
        Assert.Contains("Attestation:SelfSignedFolder", exception.Message);
        Assert.DoesNotContain(install.AttestationFolder, exception.Message);
        Assert.DoesNotContain(install.Folder.DirectoryPath, exception.Message);
    }

    /// <summary>
    ///     Test that the key check fails for an attestation provider that is not supported, naming the setting.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_UnsupportedAttestationProvider_FailsNamingTheSetting()
    {
        // Arrange: an installation with an attestation provider that does not exist
        using var install = new KeysInstall();
        install.AddToken(1, KeysInstall.TokenText);
        using var configuration = install.Configure(new KeyValuePair<string, string?>("Attestation__Provider", "Pfx"));

        // Act
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: the setting and the value are named, with all three providers
        Assert.Contains("Attestation:Provider 'Pfx' is not supported", exception.Message);
        Assert.Contains("Use SelfSigned, CertificateStore or File.", exception.Message);
    }

    /// <summary>
    ///     Test that the key check describes the attestation key of the File provider by subject, thumbprint and
    ///     validity, warns that the certificate ends soon, names no folder and never the password, and changes nothing.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_FileProviderKey_DescribesKeyAndExpiryWarningWithoutFolderOrPassword()
    {
        // Arrange: a protected PFX that ends in 30 days, with its password in the environment form of the setting
        using var certificates = new TemporaryDirectory();
        var pfx = KeyRingTestCertificates.WritePfx(certificates, "attest", KeysInstall.Password);
        using var install = new KeysInstall();
        install.AddToken(1, KeysInstall.TokenText);
        using var configuration = install.Configure(
            new("Attestation__Provider", "File"),
            new("Attestation__CertificatePath", pfx),
            new("Attestation__CertificatePassword", KeysInstall.Password));
        var before = install.Snapshot();

        // Act
        var detail = await ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken);

        // Assert: the public description, the expiry warning, no folder, no password, and nothing was changed
        Assert.Contains("attestation key File: subject 'CN=attest'", detail);
        Assert.Contains(", thumbprint ", detail);
        Assert.Contains("warning: The signing certificate ", detail);
        Assert.Contains(" expires on ", detail);
        Assert.DoesNotContain(certificates.DirectoryPath, detail);
        Assert.DoesNotContain(KeysInstall.Password, detail);
        Assert.Equal(before, install.Snapshot());
    }

    /// <summary>
    ///     Test that the key check fails when the PFX file of the File provider needs a password that is not set,
    ///     naming the file and the two settings but not the folder.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_FileProviderWithoutPassword_FailsNamingTheFileAndSettings()
    {
        // Arrange: a protected PFX and no password setting
        using var certificates = new TemporaryDirectory();
        var pfx = KeyRingTestCertificates.WritePfx(certificates, "attest", KeysInstall.Password);
        using var install = new KeysInstall();
        install.AddToken(1, KeysInstall.TokenText);
        using var configuration = install.Configure(
            new("Attestation__Provider", "File"),
            new("Attestation__CertificatePath", pfx));

        // Act
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: the file name and both settings, not the folder
        Assert.Contains("The certificate file 'attest.pfx' needs a password.", exception.Message);
        Assert.Contains("Attestation:CertificatePassword or Attestation:CertificatePasswordFile", exception.Message);
        Assert.DoesNotContain(certificates.DirectoryPath, exception.Message);
    }

    /// <summary>
    ///     Test that the key check fails when the certificate file of the File provider does not exist, naming the
    ///     file without its folder.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_FileProviderMissingFile_FailsNamingTheFileWithoutFolder()
    {
        // Arrange: a path to a file that does not exist
        using var certificates = new TemporaryDirectory();
        using var install = new KeysInstall();
        install.AddToken(1, KeysInstall.TokenText);
        using var missingFile = install.Configure(
            new("Attestation__Provider", "File"),
            new("Attestation__CertificatePath", certificates.GetFilePath("gone.pfx")));

        // Act
        var fileException = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: the file name, not the folder
        Assert.Contains("Attestation:CertificatePath 'gone.pfx' could not be loaded: the file does not exist.", fileException.Message);
        Assert.DoesNotContain(certificates.DirectoryPath, fileException.Message);
    }

    /// <summary>
    ///     Test that the key check fails for the File provider with no certificate path, naming the setting.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_FileProviderWithoutPath_FailsNamingTheSetting()
    {
        // Arrange: the File provider and no path
        using var install = new KeysInstall();
        install.AddToken(1, KeysInstall.TokenText);
        using var configuration = install.Configure(new KeyValuePair<string, string?>("Attestation__Provider", "File"));

        // Act
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: the setting is named
        Assert.Contains("Attestation:CertificatePath is not set; it is required for the File provider.", exception.Message);
    }

    /// <summary>
    ///     Test that the key check fails for the File provider with a wrong password, without the password and
    ///     without the folder.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_FileProviderWrongPassword_FailsWithoutPasswordOrFolder()
    {
        // Arrange: a protected PFX and another password
        using var certificates = new TemporaryDirectory();
        var pfx = KeyRingTestCertificates.WritePfx(certificates, "attest", KeysInstall.Password);
        using var install = new KeysInstall();
        install.AddToken(1, KeysInstall.TokenText);
        using var configuration = install.Configure(
            new("Attestation__Provider", "File"),
            new("Attestation__CertificatePath", pfx),
            new("Attestation__CertificatePassword", "some-other-password"));

        // Act
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: wrong password, and neither password nor folder in the message
        Assert.Contains("wrong password", exception.Message);
        Assert.DoesNotContain("some-other-password", exception.Message);
        Assert.DoesNotContain(KeysInstall.Password, exception.Message);
        Assert.DoesNotContain(certificates.DirectoryPath, exception.Message);
    }

    /// <summary>
    ///     Test that the key check fails when a trusted key is required and the certificate of the File provider is
    ///     self-signed, as for every provider.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_FileProviderUntrustedKeyWithRequireTrustedKey_Fails()
    {
        // Arrange: a self-signed PFX and the policy on
        using var certificates = new TemporaryDirectory();
        var pfx = KeyRingTestCertificates.WritePfx(certificates, "attest", KeysInstall.Password);
        using var install = new KeysInstall();
        install.AddToken(1, KeysInstall.TokenText);
        using var configuration = install.Configure(
            new("Attestation__Provider", "File"),
            new("Attestation__CertificatePath", pfx),
            new("Attestation__CertificatePassword", KeysInstall.Password),
            new("Attestation__RequireTrustedKey", "true"));

        // Act
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: the policy is named
        Assert.Contains("Attestation:RequireTrustedKey", exception.Message);
    }

    /// <summary>
    ///     Test that the key check passes for a ring that is encrypted with a certificate, and names the mode and
    ///     never the password.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_CertificateRing_PassesAndNeverPrintsThePassword()
    {
        // Arrange: a ring protected with a certificate, with one secret
        using var certificates = new TemporaryDirectory();
        var pfx = KeyRingTestCertificates.WritePfx(certificates, "ring", KeysInstall.Password);
        using var install = new KeysInstall(pfx);
        install.AddToken(1, KeysInstall.TokenText);
        using var configuration = install.Configure(
            new("DataProtection__Protection", "Auto"),
            new("DataProtection__CertificatePath", pfx),
            new("DataProtection__CertificatePassword", KeysInstall.Password),
            new("DataProtection__RequireEncryptedKeys", "true"));

        // Act
        var detail = await ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("protection Certificate", detail);
        Assert.Contains("(0 not encrypted)", detail);
        Assert.Contains("unreadable 0", detail);
        Assert.DoesNotContain(KeysInstall.Password, detail);
        Assert.DoesNotContain(pfx, detail);
    }

    /// <summary>
    ///     Test that the key check fails for an empty ring (an existing folder without key files, or no folder) next to
    ///     a database with secrets, names the folder and the number, and creates no key.
    /// </summary>
    /// <param name="removeFolder"><c>true</c> to remove the key folder, <c>false</c> to keep it empty.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidationChecks_ConfiguredKeys_EmptyRingWithSecrets_FailsAndCreatesNoKey(bool removeFolder)
    {
        // Arrange: secrets in the database, and a ring whose key files are gone
        using var install = new KeysInstall();
        install.AddToken(1, KeysInstall.TokenText);
        install.AddTotp(2);
        foreach (var file in install.KeyFiles())
        {
            File.Delete(file);
        }

        if (removeFolder)
        {
            Directory.Delete(install.KeysFolder, recursive: true);
        }

        using var configuration = install.Configure();

        // Act
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: the key folder is named, the secrets are not, and no key was created
        Assert.Contains($"Key ring '{install.KeysFolder}' is empty but the database holds 2 protected secrets", exception.Message);
        Assert.DoesNotContain(KeysInstall.TokenText, exception.Message);
        Assert.DoesNotContain("selftest.db", exception.Message);
        Assert.Empty(install.KeyFiles());
        Assert.Equal(!removeFolder, Directory.Exists(install.KeysFolder));
    }

    /// <summary>
    ///     Test that the key check fails when another ring cannot decrypt the secrets, with the counts and the ids of
    ///     the rows, and without the secrets.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_SecretsOfAnotherRing_FailsWithCountsAndIds()
    {
        // Arrange: secrets of one installation next to the ring of another one
        using var install = new KeysInstall();
        var cipher = install.AddToken(4, KeysInstall.TokenText);
        install.AddTotp(6);
        using var other = new KeysInstall();
        other.CreateKey();
        using var configuration = install.ConfigureKeysPath(other.KeysFolder);
        var before = other.Snapshot();

        // Act
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: counts and ids, no secret or ciphertext, no path, and the ring was left alone
        Assert.Contains("cannot decrypt 2 of 2 protected secrets", exception.Message);
        Assert.Contains("repository ids 4", exception.Message);
        Assert.Contains("user ids 6", exception.Message);
        Assert.DoesNotContain(KeysInstall.TokenText, exception.Message);
        Assert.DoesNotContain(cipher, exception.Message);
        Assert.DoesNotContain(install.Folder.DirectoryPath, exception.Message);
        Assert.Equal(before, other.Snapshot());
    }

    /// <summary>
    ///     Test that the key check fails when the newest key of the ring cannot be decrypted with the configured
    ///     certificate, without the password and without creating a key.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_WrongCertificate_FailsAndCreatesNoKey()
    {
        // Arrange: a ring encrypted with one certificate, and a configuration with another
        using var certificates = new TemporaryDirectory();
        var right = KeyRingTestCertificates.WritePfx(certificates, "right", KeysInstall.Password);
        var wrong = KeyRingTestCertificates.WritePfx(certificates, "wrong", KeysInstall.Password);
        using var install = new KeysInstall(right);
        install.AddToken(1, KeysInstall.TokenText);
        using var configuration = install.Configure(
            new("DataProtection__Protection", "Auto"),
            new("DataProtection__CertificatePath", wrong),
            new("DataProtection__CertificatePassword", KeysInstall.Password));
        var files = install.KeyFiles();

        // Act
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: the newest key is named as the problem, the password is not shown, and no key was added
        Assert.Contains("newest key cannot be decrypted", exception.Message);
        Assert.DoesNotContain(KeysInstall.Password, exception.Message);
        Assert.Equal(files, install.KeyFiles());
    }

    /// <summary>
    ///     Test that the key check fails for a ring with unencrypted keys when encrypted keys are required.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_PlaintextRingWhenEncryptionRequired_Fails()
    {
        // Arrange: a ring of unencrypted keys, and a configuration with a certificate that requires encryption
        using var certificates = new TemporaryDirectory();
        var pfx = KeyRingTestCertificates.WritePfx(certificates, "ring", KeysInstall.Password);
        using var install = new KeysInstall();
        install.AddToken(1, KeysInstall.TokenText);
        using var configuration = install.Configure(
            new("DataProtection__Protection", "Auto"),
            new("DataProtection__CertificatePath", pfx),
            new("DataProtection__CertificatePassword", KeysInstall.Password),
            new("DataProtection__RequireEncryptedKeys", "true"));

        // Act
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: the unencrypted files are the finding, without the password
        Assert.Contains("are not encrypted", exception.Message);
        Assert.DoesNotContain(KeysInstall.Password, exception.Message);
    }

    /// <summary>
    ///     Test that the key check fails for key ring settings the server refuses, without repeating a path or a secret.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_UnusableSettings_FailsWithoutLeaking()
    {
        // Arrange: a certificate ring whose certificate file is missing
        using var install = new KeysInstall();
        var missing = Path.Combine(install.Folder.DirectoryPath, "missing.pfx");
        using var configuration = install.Configure(
            new("DataProtection__Protection", "Certificate"),
            new("DataProtection__CertificatePath", missing),
            new("DataProtection__CertificatePassword", KeysInstall.Password));

        // Act
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: the setting group is named; the certificate path and the password are not
        Assert.Contains("key ring settings (DataProtection) are not usable", exception.Message);
        Assert.DoesNotContain(missing, exception.Message);
        Assert.DoesNotContain(KeysInstall.Password, exception.Message);
    }

    /// <summary>
    ///     Test that the key check fails for an unsupported provider.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredKeys_UnsupportedProvider_Fails()
    {
        // Arrange: a configured provider that this version does not support
        using var configuration = new ConfiguredKeysEnvironment(
            new(TestDatabase.ConnectionStringVariable, "Data Source=x.db"),
            new("Database__Provider", "Oracle"));

        // Act
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredKeysCheck.RunAsync(TestContext.Current.CancellationToken));

        // Assert: the provider is named
        Assert.Contains("Oracle", exception.Message);
    }

    /// <summary>
    ///     Test that the services the checks build run their throw-away database with the fast commit settings.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_FastCommit_SelfTestServices_RelaxesTheEmbeddedDatabase()
    {
        // Arrange: the production services over an embedded database in a temporary folder
        using var folder = new TemporaryDirectory();
        var provider = SelfTestServices.Create(folder);
        try
        {
            // Act: read the commit settings of a connection of that database
            var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
            await using var context = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            var settings = await ReadCommitSettingsAsync(context.Database.GetDbConnection());

            // Assert: synchronous=OFF (0) and journal_mode=MEMORY
            Assert.Equal(("0", "memory"), settings);
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    /// <summary>
    ///     Test that the production service registration alone, which the server uses, keeps the safe defaults.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_FastCommit_ProductionServices_KeepDefaultCommitSettings()
    {
        // Arrange: the production registration over the same kind of database, without the self-test interceptor
        using var folder = new TemporaryDirectory();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(SelfTestServices.CreateSettings(folder)).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        ServerHost.AddServices(services, configuration);
        var provider = services.BuildServiceProvider();
        try
        {
            // Act: create the database and read the commit settings of a connection
            DatabaseSetup.EnsureCreated(
                provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>(),
                provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);
            var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
            await using var context = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            var settings = await ReadCommitSettingsAsync(context.Database.GetDbConnection());

            // Assert: synchronous=FULL (2) and the write-ahead log the product database uses; the relaxation never
            // reaches the product registration
            Assert.Equal(("2", "wal"), settings);
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    /// <summary>
    ///     Test that the interceptor relaxes a database inside its folder and leaves every other one as it is.
    /// </summary>
    /// <param name="database">Where the database is, relative to the allowed folder.</param>
    /// <param name="options">Extra connection string options.</param>
    /// <param name="relaxed">Whether the commit settings are expected to change.</param>
    [Theory]
    [InlineData("inside.db", "", true)]
    [InlineData("sub/inside.db", "", true)]
    [InlineData("../outside.db", "", false)]
    [InlineData("../allowed-sibling/other.db", "", false)]
    [InlineData("inside.db", "Mode=ReadOnly", false)]
    public async Task ValidationChecks_FastCommit_Interceptor_RelaxesOnlyDatabasesInsideItsFolder(
        string database,
        string options,
        bool relaxed)
    {
        // Arrange: an allowed folder next to a sibling folder whose name starts the same, and an interceptor for it
        using var root = new TemporaryDirectory();
        var allowed = Directory.CreateDirectory(root.GetFilePath("allowed")).FullName;
        Directory.CreateDirectory(root.GetFilePath("allowed-sibling"));
        Directory.CreateDirectory(Path.Combine(allowed, "sub"));
        var path = Path.GetFullPath(Path.Combine(allowed, database));
        await File.WriteAllBytesAsync(path, [], TestContext.Current.CancellationToken);
        var interceptor = new FastCommitInterceptor(allowed);
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False;{options}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        // Act: let the interceptor see the connection that was just opened
        await interceptor.ConnectionOpenedAsync(connection, null!, CancellationToken.None);
        var settings = await ReadCommitSettingsAsync(connection);

        // Assert: only a writable database inside the folder is relaxed
        Assert.Equal(relaxed ? ("0", "memory") : ("2", "delete"), settings);
    }

    /// <summary>
    ///     Test that the interceptor never touches an in-memory database, also not through the synchronous hook.
    /// </summary>
    [Fact]
    public void ValidationChecks_FastCommit_Interceptor_LeavesInMemoryDatabaseAlone()
    {
        // Arrange: an in-memory connection and an interceptor for an arbitrary folder
        using var root = new TemporaryDirectory();
        var interceptor = new FastCommitInterceptor(root.DirectoryPath);
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        // Act: let the interceptor see the connection
        interceptor.ConnectionOpened(connection, null!);

        // Assert: the commit mode of the in-memory database stays at the default (FULL)
        using var synchronous = connection.CreateCommand();
        synchronous.CommandText = "PRAGMA synchronous";
        Assert.Equal(2L, synchronous.ExecuteScalar());
    }

    /// <summary>
    ///     Test that an interceptor cannot be created without the folder that makes it safe.
    /// </summary>
    /// <param name="folder">The invalid folder value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidationChecks_FastCommit_Interceptor_RequiresAllowedFolder(string? folder)
    {
        // Act and assert: a missing folder is refused, so the guard cannot be left out
        Assert.ThrowsAny<ArgumentException>(() => new FastCommitInterceptor(folder!));
    }

    /// <summary>
    ///     Reads the commit settings of an open or closed SQLite connection.
    /// </summary>
    /// <param name="connection">The connection; opened when it is closed.</param>
    /// <returns>The values of <c>PRAGMA synchronous</c> and <c>PRAGMA journal_mode</c>.</returns>
    private static async Task<(string Synchronous, string Journal)> ReadCommitSettingsAsync(System.Data.Common.DbConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var synchronous = connection.CreateCommand();
        synchronous.CommandText = "PRAGMA synchronous";
        var sync = Convert.ToString(await synchronous.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
        await using var journal = connection.CreateCommand();
        journal.CommandText = "PRAGMA journal_mode";
        var mode = Convert.ToString(await journal.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
        return (sync, mode.ToLowerInvariant());
    }

    /// <summary>
    ///     Creates a database with the current schema in a folder.
    /// </summary>
    /// <param name="folder">The folder; the file is <c>review.db</c>.</param>
    /// <returns>The connection string of the database.</returns>
    private static string CreateDatabase(TemporaryDirectory folder)
    {
        var connectionString = $"Data Source={folder.GetFilePath("review.db")};Pooling=False";
        using var context = new ReviewCenterDbContext(
            new DbContextOptionsBuilder<ReviewCenterDbContext>().UseSqlite(connectionString).Options);
        context.Database.EnsureCreated();
        return connectionString;
    }

    /// <summary>
    ///     Drops the first table of a database.
    /// </summary>
    /// <param name="connectionString">The connection string of the database.</param>
    /// <returns>The name of the table that was dropped.</returns>
    private static string DropFirstTable(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        var table = (string)select.ExecuteScalar()!;
        using var drop = connection.CreateCommand();
        drop.CommandText = $"PRAGMA foreign_keys = OFF; DROP TABLE \"{table}\"";
        drop.ExecuteNonQuery();
        return table;
    }

    /// <summary>
    ///     Points the database configuration of the process at a value for a test and restores it afterwards.
    /// </summary>
    private sealed class ConfiguredDatabaseEnvironment : IDisposable
    {
        /// <summary>
        ///     The environment variable that holds the provider.
        /// </summary>
        private const string ProviderVariable = "Database__Provider";

        /// <summary>
        ///     The connection string variable value before the test.
        /// </summary>
        private readonly string? _connectionString = Environment.GetEnvironmentVariable(TestDatabase.ConnectionStringVariable);

        /// <summary>
        ///     The provider variable value before the test.
        /// </summary>
        private readonly string? _provider = Environment.GetEnvironmentVariable(ProviderVariable);

        /// <summary>
        ///     Initializes a new instance of the <see cref="ConfiguredDatabaseEnvironment"/> class.
        /// </summary>
        /// <param name="connectionString">The connection string to configure, or null for none.</param>
        /// <param name="provider">The provider to configure, or null to leave the default.</param>
        public ConfiguredDatabaseEnvironment(string? connectionString, string? provider = null)
        {
            Environment.SetEnvironmentVariable(TestDatabase.ConnectionStringVariable, connectionString);
            Environment.SetEnvironmentVariable(ProviderVariable, provider);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Environment.SetEnvironmentVariable(TestDatabase.ConnectionStringVariable, _connectionString);
            Environment.SetEnvironmentVariable(ProviderVariable, _provider);
        }
    }
}
