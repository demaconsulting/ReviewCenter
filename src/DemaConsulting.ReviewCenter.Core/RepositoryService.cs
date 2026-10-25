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
///     Registers git repositories and keeps their credentials, enforcing the repository rules.
/// </summary>
/// <remarks>
///     <para>
///         The registry is global and administered only by administrators: every method that shows or changes a
///         registration checks again that the acting user is an enabled administrator, whatever the caller already
///         checked. Ordinary users can only list the enabled repositories as <see cref="RepositorySummary"/> (name
///         only). A repository is never deleted, only disabled, so reviews created from it keep their reference.
///     </para>
///     <para>
///         The token is write-only. It is encrypted before it is stored, it can be kept, replaced or cleared but
///         never read back through this class, and it appears in no audit entry or message; it must also differ from
///         the repository name and the user name (case-sensitive), which do appear there; only
///         <see cref="GetAccessAsync"/>, which is internal to Core, decrypts it, for the git layer. Registrations
///         are audited (<c>repository.created</c>, <c>repository.updated</c>, <c>repository.disabled</c>,
///         <c>repository.enabled</c>) with the changed field names, never their secret values. Changes run one at
///         a time (a semaphore), so the service must be a single instance (a singleton).
///     </para>
/// </remarks>
public sealed class RepositoryService
{
    /// <summary>
    ///     The longest repository name, in characters.
    /// </summary>
    public const int MaxNameLength = 40;

    /// <summary>
    ///     The longest location, in characters.
    /// </summary>
    public const int MaxLocationLength = 500;

    /// <summary>
    ///     The longest user name, in characters.
    /// </summary>
    public const int MaxUsernameLength = 200;

    /// <summary>
    ///     The longest token, in characters.
    /// </summary>
    public const int MaxTokenLength = 1000;

    /// <summary>
    ///     The store that persists the registry.
    /// </summary>
    private readonly IRepositoryStore _store;

    /// <summary>
    ///     Encrypts and decrypts the tokens.
    /// </summary>
    private readonly ICredentialProtector _protector;

    /// <summary>
    ///     Answers who the acting user is.
    /// </summary>
    private readonly UserService _users;

    /// <summary>
    ///     The clock that supplies registration and change times.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Serializes the operations that change a registration.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    ///     Initializes a new instance of the <see cref="RepositoryService"/> class.
    /// </summary>
    /// <param name="store">The store that persists the registry. Must not be null.</param>
    /// <param name="protector">The token encryption. Must not be null.</param>
    /// <param name="users">The user service, used to re-check the administrator. Must not be null.</param>
    /// <param name="timeProvider">The clock; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public RepositoryService(
        IRepositoryStore store,
        ICredentialProtector protector,
        UserService users,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _protector = protector;
        _users = users;
        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Registers an enabled repository.
    /// </summary>
    /// <param name="name">
    ///     The name: 1 to <see cref="MaxNameLength"/> letters, digits, dot, underscore and hyphen, starting with a
    ///     letter or digit; unique without regard to case.
    /// </param>
    /// <param name="location">
    ///     The location: an <c>https://</c>, <c>http://</c> or <c>file://</c> URL without embedded credentials, or an
    ///     absolute local path.
    /// </param>
    /// <param name="username">The user name for a private https repository, or <c>null</c> or white space for none.</param>
    /// <param name="token">The token for a private https repository (needs a user name), or <c>null</c> or empty for none.</param>
    /// <param name="actor">The administrator who registers the repository. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored registration, which never carries the token.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when the actor is not an administrator or a rule is broken (see <see cref="DomainRule"/>).
    /// </exception>
    public async Task<SourceRepository> CreateAsync(
        string name,
        string location,
        string? username,
        string? token,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(actor);

        await EnsureAdminAsync(actor, cancellationToken);
        var cleanName = ValidateName(name);
        var cleanLocation = ValidateLocation(location, out var isHttps);
        var cleanUser = ValidateUsername(username);
        var cleanToken = string.IsNullOrEmpty(token) ? null : ValidateToken(token);
        if (cleanToken is not null)
        {
            ValidateTokenIsNotPublic(cleanToken, cleanName, cleanUser);
        }

        ValidateCredentialCombination(isHttps, cleanUser, cleanToken is not null);

        var encrypted = cleanToken is null ? null : _protector.Protect(cleanToken);
        var entry = new NewRepository(cleanName, cleanLocation, cleanUser, encrypted, _timeProvider.GetUtcNow());
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await _store.AddAsync(
                entry,
                stored => AuditDraft.By(
                    actor,
                    "repository.created",
                    $"repository '{stored.Name}', location '{stored.Location}', user name {(stored.Username is null ? "none" : "set")}, token {(stored.HasToken ? "set" : "none")}"),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Changes the name, location, user name or token of a repository.
    /// </summary>
    /// <param name="id">The repository identity.</param>
    /// <param name="name">The new name (same rules as <see cref="CreateAsync"/>).</param>
    /// <param name="location">The new location (same rules as <see cref="CreateAsync"/>).</param>
    /// <param name="username">The new user name, or <c>null</c> or white space for none.</param>
    /// <param name="token">What to do with the stored token. Must not be null.</param>
    /// <param name="actor">The administrator who makes the change. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored registration; when nothing changed, the unchanged registration and no audit entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when the actor is not an administrator, the repository does not exist or a rule is broken.
    /// </exception>
    public async Task<SourceRepository> UpdateAsync(
        int id,
        string name,
        string location,
        string? username,
        TokenChange token,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(actor);

        await EnsureAdminAsync(actor, cancellationToken);
        var cleanName = ValidateName(name);
        var cleanLocation = ValidateLocation(location, out var isHttps);
        var cleanUser = ValidateUsername(username);
        string? encrypted = null;
        if (token.Action == TokenAction.Replace)
        {
            if (string.IsNullOrEmpty(token.Token))
            {
                throw CredentialInvalid("Enter the new token, or choose to keep or clear the stored one.");
            }

            var validToken = ValidateToken(token.Token);
            ValidateTokenIsNotPublic(validToken, cleanName, cleanUser);
            encrypted = _protector.Protect(validToken);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await GetExistingAsync(id, cancellationToken);
            var hasToken = token.Action switch
            {
                TokenAction.Replace => true,
                TokenAction.Clear => false,
                _ => current.HasToken
            };
            ValidateCredentialCombination(isHttps, cleanUser, hasToken);
            if (token.Action == TokenAction.Keep && current.HasToken
                && (!string.Equals(current.Name, cleanName, StringComparison.Ordinal)
                    || !string.Equals(current.Username, cleanUser, StringComparison.Ordinal)))
            {
                await ValidateKeptTokenAsync(id, cleanName, cleanUser, cancellationToken);
            }

            var changes = new List<string>();
            AddIf(changes, !string.Equals(current.Name, cleanName, StringComparison.Ordinal), "name");
            AddIf(changes, !string.Equals(current.Location, cleanLocation, StringComparison.Ordinal), "location");
            AddIf(changes, !string.Equals(current.Username, cleanUser, StringComparison.Ordinal), "user name");
            AddIf(changes, token.Action == TokenAction.Replace, "token replaced");
            AddIf(changes, token.Action == TokenAction.Clear && current.HasToken, "token cleared");
            if (changes.Count == 0)
            {
                return current;
            }

            var update = new RepositoryUpdate(
                cleanName,
                cleanLocation,
                cleanUser,
                current.Enabled,
                new TokenChange(token.Action, encrypted),
                _timeProvider.GetUtcNow());
            return await _store.UpdateAsync(
                id,
                update,
                AuditDraft.By(actor, "repository.updated", $"repository '{cleanName}': {string.Join(", ", changes)}"),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Disables or enables a repository.
    /// </summary>
    /// <param name="id">The repository identity.</param>
    /// <param name="enabled"><c>true</c> to enable; <c>false</c> to disable.</param>
    /// <param name="actor">The administrator who makes the change. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored registration; when it already had the requested state, it is returned unchanged with no audit entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown when the actor is not an administrator or the repository does not exist.</exception>
    public async Task<SourceRepository> SetEnabledAsync(
        int id,
        bool enabled,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        await EnsureAdminAsync(actor, cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await GetExistingAsync(id, cancellationToken);
            if (current.Enabled == enabled)
            {
                return current;
            }

            var update = new RepositoryUpdate(
                current.Name,
                current.Location,
                current.Username,
                enabled,
                TokenChange.Keep,
                _timeProvider.GetUtcNow());
            return await _store.UpdateAsync(
                id,
                update,
                AuditDraft.By(actor, enabled ? "repository.enabled" : "repository.disabled", $"repository '{current.Name}'"),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Lists every repository, enabled or not, for the administration page.
    /// </summary>
    /// <param name="actor">The administrator who asks. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The registrations ordered by name; none carries a token.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ActorNotAllowed"/> when the actor is not an administrator.</exception>
    public async Task<IReadOnlyList<SourceRepository>> ListAsync(Actor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        await EnsureAdminAsync(actor, cancellationToken);
        return await _store.ListAsync(cancellationToken);
    }

    /// <summary>
    ///     Lists the enabled repositories an ordinary user may choose from.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The identity and name of each enabled repository, ordered by name; nothing about location or credentials.</returns>
    public async Task<IReadOnlyList<RepositorySummary>> ListEnabledAsync(CancellationToken cancellationToken = default)
    {
        var all = await _store.ListAsync(cancellationToken);
        return all.Where(r => r.Enabled).Select(r => new RepositorySummary(r.Id, r.Name)).ToList();
    }

    /// <summary>
    ///     Reads what the git layer needs to reach an enabled repository, including the decrypted token.
    /// </summary>
    /// <param name="id">The repository identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The access data; it must stay in server-side code.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.RepositoryNotFound"/>, <see cref="DomainRule.RepositoryDisabled"/> or
    ///     <see cref="DomainRule.CredentialsUnreadable"/>.
    /// </exception>
    internal async Task<RepositoryAccess> GetAccessAsync(int id, CancellationToken cancellationToken = default)
    {
        // The gate keeps a concurrent update from changing the registration between the two reads, which could pair
        // the location of one version with the token of another
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var repository = await GetExistingAsync(id, cancellationToken);
            if (!repository.Enabled)
            {
                throw new DomainRuleException(
                    DomainRule.RepositoryDisabled,
                    $"The repository '{repository.Name}' is disabled.");
            }

            string? token = null;
            if (repository.HasToken)
            {
                var encrypted = await _store.GetEncryptedTokenAsync(id, cancellationToken);
                if (encrypted is not null)
                {
                    token = UnprotectToken(repository.Name, encrypted);
                }
            }

            return new RepositoryAccess(repository.Id, repository.Name, repository.Location, repository.Username, token);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Decrypts a stored token.
    /// </summary>
    /// <param name="repositoryName">The repository name, for the message.</param>
    /// <param name="encrypted">The stored text.</param>
    /// <returns>The token.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.CredentialsUnreadable"/> when it cannot be decrypted.</exception>
    private string UnprotectToken(string repositoryName, string encrypted)
    {
        try
        {
            return _protector.Unprotect(encrypted);
        }
        catch (DomainRuleException ex) when (ex.Rule == DomainRule.CredentialsUnreadable)
        {
            throw new DomainRuleException(
                DomainRule.CredentialsUnreadable,
                $"The stored token for repository '{repositoryName}' cannot be read (the key ring changed). An administrator must enter the token again.");
        }
    }

    /// <summary>
    ///     Checks that the stored token, which is kept, is not the same text as the new name or user name.
    /// </summary>
    /// <param name="id">The repository identity.</param>
    /// <param name="name">The new repository name.</param>
    /// <param name="username">The new user name, or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the check is done.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.RepositoryCredentialInvalid"/> when the kept token equals the name or user
    ///     name. A token that cannot be decrypted cannot be compared and is no obstacle: it is useless until replaced.
    /// </exception>
    private async Task ValidateKeptTokenAsync(int id, string name, string? username, CancellationToken cancellationToken)
    {
        var encrypted = await _store.GetEncryptedTokenAsync(id, cancellationToken);
        if (encrypted is null)
        {
            return;
        }

        string stored;
        try
        {
            stored = _protector.Unprotect(encrypted);
        }
        catch (DomainRuleException ex) when (ex.Rule == DomainRule.CredentialsUnreadable)
        {
            return;
        }

        ValidateTokenIsNotPublic(stored, name, username);
    }

    /// <summary>
    ///     Checks that a token is not the same text as the repository name or the user name, because those are
    ///     written to audit entries and messages.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="name">The repository name.</param>
    /// <param name="username">The user name, or <c>null</c>.</param>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.RepositoryCredentialInvalid"/> when it is the same text.</exception>
    private static void ValidateTokenIsNotPublic(string token, string name, string? username)
    {
        if (string.Equals(token, name, StringComparison.Ordinal) || string.Equals(token, username, StringComparison.Ordinal))
        {
            throw CredentialInvalid("A token must not be the same as the repository name or the user name, because those are shown in messages and the audit trail.");
        }
    }

    /// <summary>
    ///     Adds a text to a list when a condition holds.
    /// </summary>
    /// <param name="list">The list.</param>
    /// <param name="condition">The condition.</param>
    /// <param name="text">The text to add.</param>
    private static void AddIf(List<string> list, bool condition, string text)
    {
        if (condition)
        {
            list.Add(text);
        }
    }

    /// <summary>
    ///     Validates and normalizes a repository name.
    /// </summary>
    /// <param name="name">The name as entered.</param>
    /// <returns>The trimmed name.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.RepositoryNameInvalid"/> when it is not valid.</exception>
    private static string ValidateName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is 0 or > MaxNameLength
            || !char.IsAsciiLetterOrDigit(trimmed[0])
            || !trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
        {
            throw new DomainRuleException(
                DomainRule.RepositoryNameInvalid,
                $"A repository name must be 1 to {MaxNameLength} characters: letters, digits, dot, underscore and hyphen, starting with a letter or digit.");
        }

        return trimmed;
    }

    /// <summary>
    ///     Validates and normalizes a repository location.
    /// </summary>
    /// <param name="location">The location as entered.</param>
    /// <param name="isHttps">Receives whether the location is an https URL.</param>
    /// <returns>The trimmed location.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.RepositoryLocationInvalid"/> when it is not valid.</exception>
    private static string ValidateLocation(string location, out bool isHttps)
    {
        isHttps = false;
        var trimmed = location.Trim();
        const string forms = "Use an https://, http:// or file:// URL, or an absolute local path.";
        if (trimmed.Length == 0 || trimmed.Length > MaxLocationLength || trimmed.Any(char.IsControl) || trimmed[0] == '-')
        {
            throw LocationInvalid($"A location must be 1 to {MaxLocationLength} characters without control characters. {forms}");
        }

        var isUrl = trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
        if (isUrl)
        {
            if (trimmed.Any(char.IsWhiteSpace) || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                throw LocationInvalid($"The location is not a valid URL. {forms}");
            }

            if (uri.UserInfo.Length > 0)
            {
                throw LocationInvalid("The location must not contain a user name or password; enter the credentials in their own fields.");
            }

            if (trimmed.Contains('?') || trimmed.Contains('#'))
            {
                throw LocationInvalid("The location must not contain a query string or fragment; enter the credentials in their own fields.");
            }

            if (uri.Scheme != Uri.UriSchemeFile && uri.Host.Length == 0)
            {
                throw LocationInvalid($"The location has no host name. {forms}");
            }

            isHttps = uri.Scheme == Uri.UriSchemeHttps;
            return trimmed;
        }

        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw LocationInvalid($"The location is not supported (ssh, scp-style and other transports are not allowed). {forms}");
        }

        return trimmed;
    }

    /// <summary>
    ///     Validates and normalizes a user name.
    /// </summary>
    /// <param name="username">The user name as entered, or <c>null</c>.</param>
    /// <returns>The trimmed user name, or <c>null</c> when none was given.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.RepositoryCredentialInvalid"/> when it is not valid.</exception>
    private static string? ValidateUsername(string? username)
    {
        var trimmed = username?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > MaxUsernameLength || trimmed.Any(char.IsControl) || trimmed.Contains(':', StringComparison.Ordinal))
        {
            throw CredentialInvalid($"A user name must be at most {MaxUsernameLength} characters without control characters or a colon.");
        }

        return trimmed;
    }

    /// <summary>
    ///     Validates a token.
    /// </summary>
    /// <param name="token">The token as entered; it is not trimmed.</param>
    /// <returns>The token.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.RepositoryCredentialInvalid"/> when it is not valid.</exception>
    private static string ValidateToken(string token)
    {
        if (token.Length > MaxTokenLength || token.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            throw CredentialInvalid($"A token must be 1 to {MaxTokenLength} characters without spaces or control characters.");
        }

        return token;
    }

    /// <summary>
    ///     Checks that the user name and token fit the location and each other.
    /// </summary>
    /// <param name="isHttps">Whether the location is an https URL.</param>
    /// <param name="username">The user name, or <c>null</c>.</param>
    /// <param name="hasToken">Whether a token will be stored.</param>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.RepositoryCredentialInvalid"/> when they do not fit.</exception>
    private static void ValidateCredentialCombination(bool isHttps, string? username, bool hasToken)
    {
        if ((username is not null || hasToken) && !isHttps)
        {
            throw CredentialInvalid("A user name and token can only be used with an https:// location.");
        }

        if (hasToken && username is null)
        {
            throw CredentialInvalid("A token needs a user name.");
        }
    }

    /// <summary>
    ///     Creates the exception for an invalid credential.
    /// </summary>
    /// <param name="message">The operator-readable explanation.</param>
    /// <returns>The exception to throw.</returns>
    private static DomainRuleException CredentialInvalid(string message) =>
        new(DomainRule.RepositoryCredentialInvalid, message);

    /// <summary>
    ///     Creates the exception for an invalid location.
    /// </summary>
    /// <param name="message">The operator-readable explanation.</param>
    /// <returns>The exception to throw.</returns>
    private static DomainRuleException LocationInvalid(string message) =>
        new(DomainRule.RepositoryLocationInvalid, message);

    /// <summary>
    ///     Reads a repository that must exist.
    /// </summary>
    /// <param name="id">The repository identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The repository.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.RepositoryNotFound"/> when there is none.</exception>
    private async Task<SourceRepository> GetExistingAsync(int id, CancellationToken cancellationToken)
    {
        return await _store.GetAsync(id, cancellationToken)
            ?? throw new DomainRuleException(DomainRule.RepositoryNotFound, "The repository does not exist.");
    }

    /// <summary>
    ///     Checks that the acting user is an enabled administrator.
    /// </summary>
    /// <param name="actor">The acting user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the actor is known to be allowed.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ActorNotAllowed"/> when the actor is not allowed.</exception>
    private async Task EnsureAdminAsync(Actor actor, CancellationToken cancellationToken)
    {
        var user = await _users.GetAsync(actor.Username, cancellationToken);
        if (user is null || !user.Enabled || user.Role != UserRole.Admin)
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "Your account is not allowed to make this change.");
        }
    }
}
