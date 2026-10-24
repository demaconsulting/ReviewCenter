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

using System.Runtime.CompilerServices;

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     Keeps every test that starts the application away from the real per-user application data folder.
/// </summary>
/// <remarks>
///     The product default database lives under the user's local application data, which a test run must
///     never create or modify. A module initializer points the whole test process (and every server
///     process it launches, because child processes inherit the environment) at a private temporary
///     folder through the <c>Database__ConnectionString</c> configuration variable. Tests that need a
///     database of their own ask for a fresh file with <see cref="NewConnectionString"/>.
/// </remarks>
internal static class TestDatabase
{
    /// <summary>
    ///     Name of the environment variable that supplies <c>Database:ConnectionString</c>.
    /// </summary>
    public const string ConnectionStringVariable = "Database__ConnectionString";

    /// <summary>
    ///     The private folder that holds every database file created by this test run.
    /// </summary>
    private static readonly string _root = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    ///     Creates a connection string for a new, empty database file inside the private folder.
    /// </summary>
    /// <returns>A SQLite connection string with pooling off so the file can be deleted afterwards.</returns>
    public static string NewConnectionString()
    {
        Directory.CreateDirectory(_root);
        return $"Data Source={Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db")};Pooling=False";
    }

    /// <summary>
    ///     Redirects the process-wide default database into the private folder and schedules its removal.
    /// </summary>
    /// <remarks>
    ///     Runs once when the test assembly loads, before any test starts a server.
    /// </remarks>
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable(ConnectionStringVariable, NewConnectionString());

        // Best-effort cleanup: files that are still locked are left for the operating system's temp cleanup
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Ignored: the folder is under the temp directory
            }
            catch (UnauthorizedAccessException)
            {
                // Ignored: the folder is under the temp directory
            }
        };
    }
}
