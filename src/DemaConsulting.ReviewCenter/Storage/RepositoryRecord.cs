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
///     The database row of a registered repository, including the encrypted token.
/// </summary>
/// <remarks>
///     The row is internal to the storage layer: it is the only type that holds the encrypted token, and the store
///     turns it into a <see cref="DemaConsulting.ReviewCenter.Core.SourceRepository"/> (which has no token member)
///     before anything leaves the layer. The type is a plain holder and is not thread-safe.
/// </remarks>
internal sealed class RepositoryRecord
{
    /// <summary>
    ///     Gets or sets the identity assigned by the database.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    ///     Gets or sets the identity of the project the repository belongs to.
    /// </summary>
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the name, unique within the project and compared without regard to case.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the location git reads from.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the user name, or <c>null</c>.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    ///     Gets or sets the encrypted token, or <c>null</c> when there is none.
    /// </summary>
    public string? EncryptedToken { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the repository can be selected.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets the registration time (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    ///     Gets or sets the time of the last change (UTC).
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
