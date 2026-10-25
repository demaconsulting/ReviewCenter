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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     The persistence contract for the repository registry, implemented by the storage layer.
/// </summary>
/// <remarks>
///     Every method that changes data takes an audit draft and saves it in the same transaction as the change.
///     The store enforces only what it must (a unique name, compared without regard to case); every other rule
///     lives in <see cref="RepositoryService"/>. Tokens arrive already encrypted and are never returned in a
///     <see cref="SourceRepository"/>; <see cref="GetEncryptedTokenAsync"/> is the only way to read one back.
/// </remarks>
public interface IRepositoryStore
{
    /// <summary>
    ///     Adds a repository.
    /// </summary>
    /// <param name="repository">The repository to add.</param>
    /// <param name="audit">Builds the audit draft from the stored repository; saved in the same transaction. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored repository with its assigned identity.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.RepositoryNameTaken"/> when the name exists.</exception>
    Task<SourceRepository> AddAsync(
        NewRepository repository,
        Func<SourceRepository, AuditDraft> audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads one repository.
    /// </summary>
    /// <param name="id">The repository identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The repository, or <c>null</c> when there is none.</returns>
    Task<SourceRepository?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads the encrypted token of a repository.
    /// </summary>
    /// <param name="id">The repository identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The encrypted token, or <c>null</c> when there is none or no such repository.</returns>
    Task<string?> GetEncryptedTokenAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists all repositories.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>All repositories ordered by name.</returns>
    Task<IReadOnlyList<SourceRepository>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stores the new state of an existing repository.
    /// </summary>
    /// <param name="id">The repository identity.</param>
    /// <param name="update">The new state; the token is kept, replaced or cleared as it says.</param>
    /// <param name="audit">The audit draft of the change, saved in the same transaction. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored repository.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.RepositoryNotFound"/> or <see cref="DomainRule.RepositoryNameTaken"/>.
    /// </exception>
    Task<SourceRepository> UpdateAsync(
        int id,
        RepositoryUpdate update,
        AuditDraft audit,
        CancellationToken cancellationToken = default);
}
