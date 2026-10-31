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

using Microsoft.Data.Sqlite;

namespace DemaConsulting.ReviewCenter.Tests.Hosting;

/// <summary>
///     Puts protected secrets straight into the tables of a test database, the way an earlier run of the server left
///     them, so that the tests of the restore detection need no project, user or repository setup.
/// </summary>
/// <remarks>
///     The rows are inserted with plain SQL and foreign keys off: the restore detection reads only the id and the
///     protected text, so the parent rows are irrelevant. The class is stateless.
/// </remarks>
internal static class SecretSeed
{
    /// <summary>
    ///     Adds a repository row with a protected token.
    /// </summary>
    /// <param name="connectionString">The connection string of a database that has the current schema.</param>
    /// <param name="id">The id of the repository.</param>
    /// <param name="protectedToken">The protected token as stored.</param>
    public static void AddRepositoryToken(string connectionString, int id, string protectedToken) =>
        Execute(
            connectionString,
            "INSERT INTO \"Repositories\" (\"Id\", \"ProjectId\", \"Name\", \"Location\", \"EncryptedToken\", \"Enabled\", \"CreatedAt\", \"UpdatedAt\") " +
            "VALUES ($id, 1, $name, 'https://example.invalid/repo.git', $token, 1, '2024-01-01 00:00:00+00:00', '2024-01-01 00:00:00+00:00')",
            ("$id", id),
            ("$name", $"repo-{id}"),
            ("$token", protectedToken));

    /// <summary>
    ///     Adds a second-factor row with a protected secret.
    /// </summary>
    /// <param name="connectionString">The connection string of a database that has the current schema.</param>
    /// <param name="userId">The id of the user.</param>
    /// <param name="protectedSecret">The protected secret as stored.</param>
    public static void AddTotpSecret(string connectionString, int userId, string protectedSecret) =>
        Execute(
            connectionString,
            "INSERT INTO \"UserTotps\" (\"UserId\", \"ProtectedSecret\", \"Created\", \"ConfirmedAt\", \"LastAcceptedStep\") " +
            "VALUES ($id, $secret, '2024-01-01 00:00:00+00:00', '2024-01-01 00:00:00+00:00', 0)",
            ("$id", userId),
            ("$secret", protectedSecret));

    /// <summary>
    ///     Runs one statement with parameters, with foreign keys off.
    /// </summary>
    /// <param name="connectionString">The connection string of the database.</param>
    /// <param name="sql">The statement.</param>
    /// <param name="parameters">The named parameters and their values.</param>
    private static void Execute(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString) { ForeignKeys = false, Pooling = false };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }
}
