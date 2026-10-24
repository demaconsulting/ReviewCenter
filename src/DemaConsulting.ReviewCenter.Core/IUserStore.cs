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
///     The persistence contract for users, implemented by the storage layer.
/// </summary>
/// <remarks>
///     Users have their own store so the review store stays about reviews. The store enforces only what
///     it must (a unique user name, compared without regard to case); every other rule lives in
///     <see cref="UserService"/>. Implementations must return a new <see cref="User"/> object on every
///     read, so a caller can change it and pass it to <see cref="UpdateAsync"/> without touching shared state.
/// </remarks>
public interface IUserStore
{
    /// <summary>
    ///     Adds a user.
    /// </summary>
    /// <param name="user">The user to add; <see cref="User.Id"/> is ignored.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored user with its assigned identity.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.UserNameTaken"/> when the user name exists.</exception>
    Task<User> AddAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads a user by user name.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The user, or <c>null</c> when there is none.</returns>
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists all users.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>All users ordered by user name.</returns>
    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stores the changed state of an existing user.
    /// </summary>
    /// <param name="user">The user, identified by <see cref="User.Id"/>; all stored fields are replaced.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the user is stored.</returns>
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);
}
