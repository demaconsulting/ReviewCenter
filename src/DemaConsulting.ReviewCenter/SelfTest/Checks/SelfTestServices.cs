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

using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     Builds the production services over a private, embedded SQLite database inside a temporary folder, so a
///     check exercises the real services without touching the configured database, key folders or port.
/// </summary>
/// <remarks>
///     The services are registered by the same <see cref="ServerHost.AddServices"/> the server uses, so the check
///     runs the production code. Only the configuration differs: it is an in-memory set that points the database,
///     the Data Protection keys, the git cache and the attestation key at the temporary folder. The
///     <c>appsettings.json</c> file and the environment variables are deliberately not read, which is what makes
///     the self-validation independent of how the server is configured. The class is stateless and thread-safe.
/// </remarks>
internal static class SelfTestServices
{
    /// <summary>
    ///     The password of the throw-away user; it satisfies the production password policy and protects nothing.
    /// </summary>
    public const string SelfTestSecret = "correct horse battery";

    /// <summary>
    ///     Creates the service provider and prepares the embedded database.
    /// </summary>
    /// <param name="folder">The temporary folder that holds everything the services write. Must not be null.</param>
    /// <returns>The provider. The caller owns it and must dispose it before the folder is deleted.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder"/> is null.</exception>
    /// <remarks>
    ///     Side effects: creates the database file and its schema in <paramref name="folder"/>. Pooling is off so
    ///     the file is released when the provider is disposed and can be deleted afterwards.
    /// </remarks>
    public static ServiceProvider Create(TemporaryDirectory folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        // An in-memory configuration only: appsettings.json and the environment are never consulted
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(CreateSettings(folder))
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        ServerHost.AddServices(services, configuration);

        // Only the speed of a commit changes, not what is stored; the interceptor acts on this folder only
        // (see FastCommitInterceptor)
        var interceptor = new FastCommitInterceptor(folder.DirectoryPath);
        services.ConfigureDbContext<ReviewCenterDbContext>(builder => builder.AddInterceptors(interceptor));

        var provider = services.BuildServiceProvider();
        try
        {
            DatabaseSetup.EnsureCreated(
                provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>(),
                provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);
            return provider;
        }
        catch
        {
            // The caller never sees the provider when the database cannot be prepared
            provider.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Creates the settings that point every place the services write at the temporary folder.
    /// </summary>
    /// <param name="folder">The temporary folder. Must not be null.</param>
    /// <returns>The configuration keys and values: database, key ring, git cache and attestation key.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder"/> is null.</exception>
    /// <remarks>
    ///     The host check applies the same settings to a real web host, so both checks share one definition of
    ///     "everything inside the temporary folder". The key ring is only protected by the permissions of its
    ///     temporary folder (<c>DataProtection:Protection</c> is <c>PermissionsOnly</c>), so the checks never use the
    ///     key store of the operating system or a certificate of the operator.
    /// </remarks>
    public static Dictionary<string, string?> CreateSettings(TemporaryDirectory folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new Dictionary<string, string?>
        {
            ["Database:Provider"] = DatabaseSetup.SqliteProvider,
            ["Database:ConnectionString"] = $"Data Source={folder.GetFilePath("selftest.db")};Pooling=False",
            ["DataProtection:KeysPath"] = Path.Combine(folder.DirectoryPath, "keys"),
            ["DataProtection:Protection"] = "PermissionsOnly",
            ["Git:CachePath"] = Path.Combine(folder.DirectoryPath, "git-cache"),
            ["Git:TimeoutSeconds"] = "60",
            ["Attestation:SelfSignedFolder"] = Path.Combine(folder.DirectoryPath, "attestation")
        };
    }
}
