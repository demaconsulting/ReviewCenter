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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Well-known signed-in users for tests that record who did something.
/// </summary>
internal static class TestActors
{
    /// <summary>
    ///     User <c>ann</c>, displayed as <c>Ann</c>.
    /// </summary>
    public static Actor Ann { get; } = new("ann", "Ann");

    /// <summary>
    ///     User <c>bob</c>, displayed as <c>Bob</c>.
    /// </summary>
    public static Actor Bob { get; } = new("bob", "Bob");

    /// <summary>
    ///     User <c>cy</c>, displayed as <c>Cy</c>.
    /// </summary>
    public static Actor Cy { get; } = new("cy", "Cy");

    /// <summary>
    ///     User <c>dee</c>, displayed as <c>Dee</c>.
    /// </summary>
    public static Actor Dee { get; } = new("dee", "Dee");

    /// <summary>
    ///     User <c>eve</c>, displayed as <c>Eve</c>.
    /// </summary>
    public static Actor Eve { get; } = new("eve", "Eve");

    /// <summary>
    ///     User <c>author</c>, displayed as <c>Author</c>: creates reviews in tests and is never a project member
    ///     or a reviewer, so that the independence of reviewers does not get in the way of unrelated tests.
    /// </summary>
    public static Actor Author { get; } = new("author", "Author");
}

/// <summary>
///     A trivial <see cref="IPasswordHasher"/> for the user-rule tests: not secure, and fast and predictable.
/// </summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    /// <inheritdoc />
    public string Hash(string password)
    {
        return "hash:" + password;
    }

    /// <inheritdoc />
    public bool Verify(string hash, string password)
    {
        return hash == "hash:" + password;
    }
}

/// <summary>
///     In-memory <see cref="IUserStore"/> used to test <see cref="UserService"/> without a database.
/// </summary>
/// <remarks>
///     Like the real store it keeps user names unique without regard to case and returns a new
///     <see cref="User"/> object on every read, so changing a returned user changes nothing until it is stored.
/// </remarks>
internal sealed class FakeUserStore : IUserStore
{
    /// <summary>
    ///     The stored users.
    /// </summary>
    private readonly List<User> _users = [];

    /// <summary>
    ///     The store that receives the membership of <see cref="AddToProjectAsync"/>, or <c>null</c> when no test
    ///     needs the membership.
    /// </summary>
    private readonly FakeReviewStore? _reviews;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeUserStore"/> class.
    /// </summary>
    /// <param name="reviews">
    ///     The store that keeps the memberships; <see cref="AddToProjectAsync"/> adds its membership there. May be
    ///     <c>null</c> for tests that never add a user to a project.
    /// </param>
    public FakeUserStore(FakeReviewStore? reviews = null)
    {
        _reviews = reviews;
        reviews?.IsUserEnabled = IsEnabled;
    }

    /// <summary>
    ///     Gets the audit drafts received so far, in the order the changes were made; unaudited updates add none.
    /// </summary>
    public List<AuditDraft> Audits { get; } = [];

    /// <summary>
    ///     Gets a value indicating whether the stored user with the identity is enabled.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <returns><c>true</c> when the user exists and is enabled.</returns>
    private bool IsEnabled(int userId)
    {
        return _users.Any(user => user.Id == userId && user.Enabled);
    }

    /// <inheritdoc />
    public Task<User> AddAsync(
        User user,
        Func<User, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        if (_users.Any(existing => string.Equals(existing.Username, user.Username, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainRuleException(DomainRule.UserNameTaken, $"The user name '{user.Username}' is taken.");
        }

        _users.Add(Copy(user, _users.Count + 1));
        var stored = Copy(_users[^1], _users[^1].Id);
        Audits.Add(audit(stored));
        return Task.FromResult(stored);
    }

    /// <inheritdoc />
    public async Task<User> AddToProjectAsync(
        User user,
        int projectId,
        ProjectRole role,
        Func<User, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        // The user, the membership and the one audit entry are stored together or not at all
        var stored = await AddAsync(user, audit, cancellationToken);
        _reviews?.AddMembershipWithoutAudit(new ProjectMembership(stored.Id, projectId, role));
        return stored;
    }

    /// <inheritdoc />
    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = _users.FirstOrDefault(
            existing => string.Equals(existing.Username, username, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(user is null ? null : Copy(user, user.Id));
    }

    /// <inheritdoc />
    public Task<User?> GetByExternalAsync(string issuer, string subject, CancellationToken cancellationToken = default)
    {
        var user = _users.FirstOrDefault(
            existing => existing.ExternalIssuer == issuer && existing.ExternalSubject == subject);
        return Task.FromResult(user is null ? null : Copy(user, user.Id));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<User> list = _users
            .OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
            .Select(user => Copy(user, user.Id))
            .ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task UpdateAsync(User user, AuditDraft? audit, CancellationToken cancellationToken = default)
    {
        var index = _users.FindIndex(existing => existing.Id == user.Id);
        _users[index] = Copy(user, user.Id);
        if (audit is not null)
        {
            Audits.Add(audit);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Copies a user and gives the copy an identity.
    /// </summary>
    /// <param name="user">The user to copy.</param>
    /// <param name="id">The identity of the copy.</param>
    /// <returns>The copy.</returns>
    private static User Copy(User user, int id)
    {
        return new User
        {
            Id = id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            IsSystemAdmin = user.IsSystemAdmin,
            Enabled = user.Enabled,
            PasswordHash = user.PasswordHash,
            FailedLoginCount = user.FailedLoginCount,
            LockoutUntil = user.LockoutUntil,
            MustChangePassword = user.MustChangePassword,
            LastSignInUtc = user.LastSignInUtc,
            Created = user.Created,
            ExternalIssuer = user.ExternalIssuer,
            ExternalSubject = user.ExternalSubject
        };
    }
}

/// <summary>
///     A <see cref="TimeProvider"/> whose instant a test can move forward.
/// </summary>
/// <param name="now">The starting instant.</param>
internal sealed class MovableTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary>
    ///     The instant reported now.
    /// </summary>
    private DateTimeOffset _now = now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>
    ///     Moves the clock forward.
    /// </summary>
    /// <param name="span">How far to move it.</param>
    public void Advance(TimeSpan span)
    {
        _now += span;
    }
}
