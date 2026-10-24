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

namespace DemaConsulting.ReviewCenter.Storage;

/// <summary>
///     The database settings read from the <c>Database</c> configuration section.
/// </summary>
/// <remarks>
///     The settings are bound by the options system, so they can come from <c>appsettings.json</c>
///     or from environment variables such as <c>Database__ConnectionString</c>. The type is a plain
///     settings holder and is not thread-safe while it is being bound.
/// </remarks>
internal sealed class DatabaseOptions
{
    /// <summary>
    ///     Gets or sets the database provider name. Only <c>Sqlite</c> is supported; the comparison
    ///     ignores case.
    /// </summary>
    public string Provider { get; set; } = DatabaseSetup.SqliteProvider;

    /// <summary>
    ///     Gets or sets the connection string, or for SQLite a plain database file path. Empty selects
    ///     the default database file in the application data folder.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
