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

using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     The opt-in check (<c>--validate --check-config</c>) that the key ring the server is configured to use fits the
///     database: the ring can be read, and every protected secret of the database can be decrypted with it.
/// </summary>
/// <remarks>
///     It answers the question that matters after a restore, before the server is started: do the keys and the
///     database belong together? The check loads the configuration the way the server does, builds the production
///     protectors, reads the key ring read-only (<see cref="KeyRingProtection.CheckRing"/>) and runs
///     <see cref="SecretScan"/> over the database. It reports the protection mode, the number of key files (and how
///     many are not encrypted) and the number of protected secrets (and how many cannot be read). It fails for
///     secrets that cannot be read, for a key ring that does not meet <c>DataProtection:RequireEncryptedKeys</c>,
///     for an empty ring next to a database with secrets, and for a ring whose newest key cannot be decrypted.
///     It never creates a key, a folder or a file: automatic key generation of Data Protection is switched off for
///     the services of the check, and the folder of the ring is only read when it exists. It never prints a key, a
///     secret, a password or any path other than the key folder; certificate settings that cannot be used are
///     reported by the setting name only. With no database (not configured, or the file does not exist yet) it is
///     skipped, as it is when the key folder does not exist yet and there is nothing it could protect. The class is
///     stateless and thread-safe.
/// </remarks>
internal static class ConfiguredKeysCheck
{
    /// <summary>
    ///     The name of the check in the report and the results file.
    /// </summary>
    public const string Name = "ReviewCenter_ConfiguredKeys";

    /// <summary>
    ///     Runs the check.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The protection mode, the key file counts and the secret counts; never a key, a secret or a path.</returns>
    /// <exception cref="ValidationSkippedException">
    ///     Thrown when no database is configured or the file does not exist yet, or the key folder does not exist yet
    ///     and the database holds no protected secret.
    /// </exception>
    /// <exception cref="ValidationFailedException">
    ///     Thrown when the key ring settings are unusable, the ring cannot be read, the ring is empty or does not
    ///     meet the settings, or a protected secret cannot be decrypted.
    /// </exception>
    public static async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        // The same configuration sources as the server: appsettings.json next to the program and the environment
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = typeof(ConfiguredKeysCheck).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });

        // Nothing configured and nothing at the default place: there is nothing to check
        var databaseSection = new DatabaseOptions();
        builder.Configuration.GetSection(DatabaseSetup.SectionName).Bind(databaseSection);
        if (string.IsNullOrWhiteSpace(databaseSection.ConnectionString) && !File.Exists(DatabaseSetup.GetDefaultDatabasePath()))
        {
            throw new ValidationSkippedException(
                "No database is configured (Database:ConnectionString is empty) and none exists at the default location.");
        }

        // The production registration, with the one change that makes the check read-only: Data Protection may not
        // create a key, whatever it is asked
        var services = new ServiceCollection();
        services.AddLogging();
        ServerHost.AddServices(services, builder.Configuration);
        services.AddOptions<KeyManagementOptions>().Configure(keys => keys.AutoGenerateKeys = false);
        await using var provider = services.BuildServiceProvider();

        var plan = provider.GetRequiredService<KeyRingPlan>();
        ValidationFailedException.ThrowUnless(
            plan.Problem is null,
            "The key ring settings (DataProtection) are not usable, and the server refuses to start with them. " +
            "Start the server or check the DataProtection settings (Protection, CertificatePath, CertificatePassword, " +
            "CertificatePasswordFile, PreviousCertificatePaths, RequireEncryptedKeys) for the reason.");

        var settings = provider.GetRequiredService<IOptions<KeyRingSettings>>().Value;
        var database = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        try
        {
            DatabaseSetup.RequireSupportedProvider(database);

            // A database that does not exist yet has no secrets: it is created at the first start
            if (DatabaseSetup.TryGetMissingDatabaseFile(database) is not null)
            {
                throw new ValidationSkippedException(
                    "The configured database file does not exist yet; it is created at the first start.");
            }

            var folder = ServerHost.ResolveKeysPath(settings, database);
            if (Directory.Exists(folder))
            {
                // Read-only: the ring is read with the keys of the plan, and a problem with it is the finding
                KeyRingProtection.CheckRing(
                    plan, provider.GetRequiredService<IKeyManager>(), folder, NullLogger.Instance);
            }

            var scan = await SecretScan.ScanAsync(
                folder,
                database,
                provider.GetRequiredService<ICredentialProtector>(),
                provider.GetRequiredService<ITotpSecretProtector>(),
                cancellationToken);

            // A folder that does not exist yet is fine for a database without secrets: the first start creates both
            if (!Directory.Exists(folder) && scan.ProtectedSecrets == 0)
            {
                throw new ValidationSkippedException(
                    "The key folder does not exist yet and the database holds no protected secret; the folder is " +
                    "created at the first start.");
            }

            return Evaluate(plan, folder, scan);
        }
        catch (InvalidOperationException ex)
        {
            // The key ring problems (never a key or the content of a file) and configuration errors (unsupported
            // provider, in-memory database); none contains the connection string
            throw new ValidationFailedException(ex.Message);
        }
        catch (SqliteException ex)
        {
            // The SQLite message names the problem, not the path or the connection string
            throw new ValidationFailedException($"The configured database could not be read: {ex.Message}");
        }
    }

    /// <summary>
    ///     Turns the scan into the report line, or the failure.
    /// </summary>
    /// <param name="plan">The key ring plan.</param>
    /// <param name="folder">The key folder; the only path that may appear in a message.</param>
    /// <param name="scan">What the scan found.</param>
    /// <returns>The report line.</returns>
    /// <exception cref="ValidationFailedException">Thrown when the ring is empty but secrets exist, or secrets cannot be read.</exception>
    private static string Evaluate(KeyRingPlan plan, string folder, SecretScanResult scan)
    {
        var summary =
            $"protection {plan.Mode}; key files {scan.KeyFiles} ({scan.PlaintextKeyFiles} not encrypted); " +
            $"protected secrets {scan.ProtectedSecrets} (repository tokens {scan.RepositoryTokens}, " +
            $"second-factor secrets {scan.TotpSecrets}); unreadable {scan.UnreadableSecrets}";

        ValidationFailedException.ThrowUnless(
            !(scan.RingIsEmpty && scan.ProtectedSecrets > 0),
            $"Key ring '{folder}' is empty but the database holds {scan.ProtectedSecrets} protected secrets. " +
            "Restore the keys folder that belongs to this database. The server refuses to start until then " +
            "(unless DataProtection:AllowNewKeyRing=true accepts that they become unreadable).");
        ValidationFailedException.ThrowUnless(
            scan.UnreadableSecrets == 0,
            $"The key ring cannot decrypt {scan.UnreadableSecrets} of {scan.ProtectedSecrets} protected secrets: " +
            $"{scan.UnreadableRepositoryTokens} repository token(s) (repository ids {SecretScan.FormatIds(scan.UnreadableRepositoryIds)}) " +
            $"and {scan.UnreadableTotpSecrets} second-factor secret(s) (user ids {SecretScan.FormatIds(scan.UnreadableUserIds)}). " +
            "Restore the key folder that belongs to this database, or enter the repository tokens again and have an " +
            "administrator reset the two-factor secret of each user.");

        return summary;
    }
}
