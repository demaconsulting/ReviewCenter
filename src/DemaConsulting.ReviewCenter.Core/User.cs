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
///     A local user account: who may sign in, whether the user is a system administrator, and the state of the sign-in protection.
/// </summary>
/// <remarks>
///     Instances are created by <see cref="UserService"/>, which enforces the rules, and are populated by
///     the storage layer. The fields that change over the life of an account (administrator flag, enabled flag, password
///     hash and the lockout state) have setters so the service can change a copy it has read and store it
///     again. The password hash must never be logged or shown. The type is not thread-safe.
/// </remarks>
public sealed class User
{
    /// <summary>
    ///     Gets the identity assigned by the store when the user is added.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    ///     Gets the user name used to sign in; unique without regard to case, 3 to
    ///     <see cref="ReviewLimits.MaxUsernameLength"/> characters of letters, digits, dot, underscore and hyphen.
    /// </summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the name shown to people (trimmed, 1 to <see cref="ReviewLimits.MaxDisplayNameLength"/> characters).
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    ///     Gets or sets a value indicating whether the user is a system administrator: may manage users, create
    ///     projects and see every project. Everything else a user may do comes from a
    ///     <see cref="ProjectMembership"/>.
    /// </summary>
    public bool IsSystemAdmin { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the user can sign in; a disabled user keeps the work already recorded.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets the salted password hash; never the password.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the number of consecutive failed password checks since the last success or lockout.
    /// </summary>
    public int FailedLoginCount { get; set; }

    /// <summary>
    ///     Gets or sets the moment until which sign-in is refused, or <c>null</c> when the user is not locked out.
    /// </summary>
    public DateTimeOffset? LockoutUntil { get; set; }

    /// <summary>
    ///     Gets the moment the account was created (UTC).
    /// </summary>
    public DateTimeOffset Created { get; init; }
}
