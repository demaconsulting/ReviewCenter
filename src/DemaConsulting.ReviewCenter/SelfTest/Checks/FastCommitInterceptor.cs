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

using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     Makes the commits of a throw-away SQLite database cheap: the journal is kept in memory and a commit does not
///     wait for the disk. It changes nothing outside the one folder it is given.
/// </summary>
/// <remarks>
///     With the default settings every commit waits for several disk flushes, which is the dominant cost of a
///     review workflow (about a hundred commits). The settings change only how long a commit takes, not what is
///     stored, and a lost file would only be a lost temporary database. Because the same settings on a real
///     database would trade durability for speed, the interceptor is guarded: it acts only on a connection that
///     is not read-only and whose database file is inside <c>allowedFolder</c>, which is the temporary folder of
///     the check that created the interceptor. Any other connection (the configured product database, an
///     in-memory database, a read-only probe) is left exactly as it is. This single class serves both the
///     embedded self-test and the test project (which sees it through <c>InternalsVisibleTo</c>), so the
///     relaxation exists in one place only. The type is immutable and thread-safe.
/// </remarks>
internal sealed class FastCommitInterceptor : DbConnectionInterceptor
{
    /// <summary>
    ///     The statements that turn the disk waits off for a connection.
    /// </summary>
    private const string Pragmas = "PRAGMA synchronous=OFF; PRAGMA journal_mode=MEMORY;";

    /// <summary>
    ///     The full path of the folder whose databases may be relaxed, without a trailing separator.
    /// </summary>
    private readonly string _allowedFolder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FastCommitInterceptor"/> class.
    /// </summary>
    /// <param name="allowedFolder">
    ///     The folder that holds the throw-away databases. Only a database file inside it is relaxed. Must not be
    ///     null, empty or white space, so the guard cannot be left out by accident.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="allowedFolder"/> is null, empty or white space.</exception>
    public FastCommitInterceptor(string allowedFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(allowedFolder);
        _allowedFolder = Path.GetFullPath(allowedFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (!ShouldRelax(connection))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldRelax(connection))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    ///     Decides whether the settings may be applied to a connection that was just opened.
    /// </summary>
    /// <param name="connection">The connection that was just opened.</param>
    /// <returns>
    ///     <c>true</c> when the connection is writable and its database file is inside the allowed folder.
    /// </returns>
    /// <remarks>
    ///     A read-only connection is skipped because its journal mode cannot be changed. A data source that is not
    ///     a plain file path (an in-memory database, a URI, nothing) can never be inside the folder, so it is
    ///     skipped too.
    /// </remarks>
    private bool ShouldRelax(DbConnection connection)
    {
        var builder = new SqliteConnectionStringBuilder(connection.ConnectionString);
        if (builder.Mode is SqliteOpenMode.ReadOnly or SqliteOpenMode.Memory
            || string.IsNullOrWhiteSpace(builder.DataSource)
            || builder.DataSource.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Compare full paths with the platform's own rules, so a ".." or a sibling folder with the same prefix
        // is never mistaken for a file inside the allowed folder
        string relative;
        try
        {
            relative = Path.GetRelativePath(_allowedFolder, Path.GetFullPath(builder.DataSource));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // ":memory:", a file: URI or another value that is not a usable path is not inside the folder
            return false;
        }

        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
