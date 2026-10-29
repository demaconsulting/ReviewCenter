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
///     In-memory <see cref="IRepositoryStore"/> used to test <see cref="RepositoryService"/> without a database.
/// </summary>
/// <remarks>
///     Like the real store it keeps names unique without regard to case, keeps the encrypted token apart from the
///     <see cref="SourceRepository"/> read model and returns new objects on every read.
/// </remarks>
internal sealed class FakeRepositoryStore : IRepositoryStore
{
    /// <summary>
    ///     The stored registrations with their encrypted tokens.
    /// </summary>
    private readonly List<(SourceRepository Repository, string? EncryptedToken)> _items = [];

    /// <summary>
    ///     Gets the audit drafts received so far, in the order the changes were made.
    /// </summary>
    public List<AuditDraft> Audits { get; } = [];

    /// <summary>
    ///     Gets or sets an action that runs once, at the start of the next <see cref="GetEncryptedTokenAsync"/>.
    /// </summary>
    public Action? BeforeGetEncryptedToken { get; set; }

    /// <summary>
    ///     Reads the encrypted token as stored, for asserting that the plain token never reached the store.
    /// </summary>
    /// <param name="id">The repository identity.</param>
    /// <returns>The stored text, or <c>null</c> when there is none.</returns>
    public string? StoredToken(int id) => _items.First(item => item.Repository.Id == id).EncryptedToken;

    /// <inheritdoc />
    public Task<SourceRepository> AddAsync(
        NewRepository repository,
        Func<SourceRepository, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        if (_items.Any(item => item.Repository.ProjectId == repository.ProjectId
            && string.Equals(item.Repository.Name, repository.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainRuleException(DomainRule.RepositoryNameTaken, $"The repository name '{repository.Name}' is taken.");
        }

        var stored = new SourceRepository(
            _items.Count + 1,
            repository.ProjectId,
            repository.Name,
            repository.Location,
            repository.Username,
            repository.EncryptedToken is not null,
            true,
            repository.CreatedAt,
            repository.CreatedAt);
        _items.Add((stored, repository.EncryptedToken));
        Audits.Add(audit(stored));
        return Task.FromResult(stored);
    }

    /// <inheritdoc />
    public Task<SourceRepository?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = _items.FirstOrDefault(i => i.Repository.Id == id);
        return Task.FromResult(item.Repository is null ? null : item.Repository with { });
    }

    /// <inheritdoc />
    public Task<string?> GetEncryptedTokenAsync(int id, CancellationToken cancellationToken = default)
    {
        // A test can use the hook to make a change at the moment between the two reads of a registration
        var hook = BeforeGetEncryptedToken;
        BeforeGetEncryptedToken = null;
        hook?.Invoke();
        var item = _items.FirstOrDefault(i => i.Repository?.Id == id);
        return Task.FromResult(item.EncryptedToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SourceRepository>> ListAsync(int projectId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SourceRepository> list = _items
            .Select(item => item.Repository)
            .Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task<SourceRepository> UpdateAsync(
        int id,
        RepositoryUpdate update,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        var index = _items.FindIndex(i => i.Repository.Id == id);
        if (index < 0)
        {
            throw new DomainRuleException(DomainRule.RepositoryNotFound, "The repository does not exist.");
        }

        if (_items.Any(i => i.Repository.Id != id
            && i.Repository.ProjectId == _items[index].Repository.ProjectId
            && string.Equals(i.Repository.Name, update.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainRuleException(DomainRule.RepositoryNameTaken, $"The repository name '{update.Name}' is taken.");
        }

        var (current, token) = _items[index];
        token = update.Token.Action switch
        {
            TokenAction.Replace => update.Token.Token,
            TokenAction.Clear => null,
            _ => token
        };
        var stored = current with
        {
            Name = update.Name,
            Location = update.Location,
            Username = update.Username,
            Enabled = update.Enabled,
            HasToken = token is not null,
            UpdatedAt = update.UpdatedAt
        };
        _items[index] = (stored, token);
        Audits.Add(audit);
        return Task.FromResult(stored);
    }
}

/// <summary>
///     A reversible <see cref="ICredentialProtector"/> for tests: not secure, but it never returns the plain text
///     and can be told to fail like a lost key ring.
/// </summary>
internal sealed class FakeCredentialProtector : ICredentialProtector
{
    /// <summary>
    ///     The prefix that marks a protected text.
    /// </summary>
    private const string Prefix = "protected:";

    /// <summary>
    ///     Gets or sets a value indicating whether <see cref="Unprotect"/> fails as if the key ring was lost.
    /// </summary>
    public bool Unreadable { get; set; }

    /// <inheritdoc />
    public string Protect(string plainText) => Prefix + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plainText));

    /// <inheritdoc />
    public string Unprotect(string protectedText)
    {
        if (Unreadable || !protectedText.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new DomainRuleException(DomainRule.CredentialsUnreadable, "The token cannot be decrypted.");
        }

        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedText[Prefix.Length..]));
    }
}

/// <summary>
///     An in-memory <see cref="IGitRepositoryProvider"/> that serves scripted revisions, changes and blobs, so the
///     Core and Web tests do not need git.
/// </summary>
internal sealed class FakeGitRepositoryProvider : IGitRepositoryProvider
{
    /// <summary>
    ///     Gets the commit each known revision name resolves to.
    /// </summary>
    public Dictionary<string, string> Revisions { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the scripted changes by baseline and head commit; the baseline is an empty text for none.
    /// </summary>
    public Dictionary<(string Baseline, string Head), GitChangeSet> Changes { get; } = [];

    /// <summary>
    ///     Gets the blob contents by blob identifier.
    /// </summary>
    public Dictionary<string, byte[]> Blobs { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the access data of every <see cref="OpenAsync"/> call, in order.
    /// </summary>
    public List<RepositoryAccess> Opened { get; } = [];

    /// <summary>
    ///     Gets or sets the exception <see cref="OpenAsync"/> throws, or <c>null</c> to succeed.
    /// </summary>
    public Exception? OpenFailure { get; set; }

    /// <summary>
    ///     Gets the number of blob reads (not size queries) made so far.
    /// </summary>
    public int BlobReads { get; private set; }

    /// <summary>
    ///     Gets the (ancestor, descendant) pairs <see cref="IGitRepository.IsAncestorAsync"/> answers <c>true</c> for;
    ///     a commit is always its own ancestor.
    /// </summary>
    public HashSet<(string Ancestor, string Descendant)> Ancestors { get; } = [];

    /// <summary>
    ///     Gets the ancestor questions that fail like an unknown commit would, as (ancestor, descendant) pairs.
    /// </summary>
    public HashSet<(string Ancestor, string Descendant)> AncestorFailures { get; } = [];

    /// <summary>
    ///     Gets the number of <see cref="IGitRepository.ListChangesAsync"/> calls made so far.
    /// </summary>
    public int ListCalls { get; private set; }

    /// <summary>
    ///     Gets or sets an action run (once) at the start of the next <see cref="IGitRepository.ListChangesAsync"/>
    ///     call, to simulate something happening elsewhere while a review is made.
    /// </summary>
    public Func<Task>? OnNextListChanges { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether every <see cref="IGitRepository.ListChangesAsync"/> call returns a
    ///     fresh copy of the scripted change set that is tracked weakly, and records before each call how many of the
    ///     earlier copies are still alive (see <see cref="AliveAtListCall"/>).
    /// </summary>
    public bool TrackChangeSets { get; set; }

    /// <summary>
    ///     Gets, for every <see cref="IGitRepository.ListChangesAsync"/> call made while <see cref="TrackChangeSets"/>
    ///     is set, how many change sets returned by earlier calls had not been collected when the call started.
    /// </summary>
    public List<int> AliveAtListCall { get; } = [];

    /// <summary>
    ///     Gets the weak references to the change sets returned while <see cref="TrackChangeSets"/> is set.
    /// </summary>
    private List<WeakReference<GitChangeSet>> Returned { get; } = [];

    /// <summary>
    ///     Scripts one commit pair: the revision names, the changes and the blob contents they need.
    /// </summary>
    /// <param name="baselineSha">The baseline commit, or <c>null</c> for none.</param>
    /// <param name="headSha">The head commit.</param>
    /// <param name="changes">The changes between them, as (path, kind, baseline content, head content).</param>
    /// <param name="skipped">The entries to report as skipped.</param>
    public void Script(
        string? baselineSha,
        string headSha,
        IEnumerable<(string Path, ChangeKind Kind, string? Baseline, string? Head)> changes,
        params SkippedEntry[] skipped)
    {
        var list = new List<GitChange>();
        foreach (var (path, kind, baseline, head) in changes)
        {
            string? baselineBlob = null;
            string? headBlob = null;
            if (baseline is not null)
            {
                baselineBlob = $"b-{baselineSha}-{path}";
                Blobs[baselineBlob] = System.Text.Encoding.UTF8.GetBytes(baseline);
            }

            if (head is not null)
            {
                headBlob = $"h-{headSha}-{path}";
                Blobs[headBlob] = System.Text.Encoding.UTF8.GetBytes(head);
            }

            list.Add(new GitChange(path, kind, baselineBlob, headBlob));
        }

        Changes[(baselineSha ?? string.Empty, headSha)] = new GitChangeSet(list, skipped);
    }

    /// <summary>
    ///     Returns a fresh copy of a scripted change set, after recording how many earlier copies are still alive.
    /// </summary>
    /// <param name="set">The scripted change set.</param>
    /// <returns>The copy, which is tracked weakly from now on.</returns>
    private GitChangeSet Track(GitChangeSet set)
    {
        // Collect first so that only sets that are still referenced count as alive
        ForcedGarbageCollection.CollectAndFinalize();
        ForcedGarbageCollection.CollectAndFinalize();
        AliveAtListCall.Add(Returned.Count(reference => reference.TryGetTarget(out _)));
        var copy = new GitChangeSet(set.Changes, set.Skipped);
        Returned.Add(new WeakReference<GitChangeSet>(copy));
        return copy;
    }

    /// <inheritdoc />
    public Task<IGitRepository> OpenAsync(RepositoryAccess access, CancellationToken cancellationToken = default)
    {
        Opened.Add(access);
        if (OpenFailure is not null)
        {
            throw OpenFailure;
        }

        return Task.FromResult<IGitRepository>(new Session(this));
    }

    /// <summary>
    ///     A session over the scripted data.
    /// </summary>
    /// <param name="owner">The provider that holds the data.</param>
    private sealed class Session(FakeGitRepositoryProvider owner) : IGitRepository
    {
        /// <inheritdoc />
        public Task<string> ResolveRevisionAsync(string revision, CancellationToken cancellationToken = default)
        {
            RevisionValidator.Validate(revision);
            return owner.Revisions.TryGetValue(revision, out var sha)
                ? Task.FromResult(sha)
                : throw new DomainRuleException(DomainRule.RevisionNotFound, $"The revision '{revision}' was not found.");
        }

        /// <inheritdoc />
        public Task<bool> IsAncestorAsync(
            string ancestorSha,
            string descendantSha,
            CancellationToken cancellationToken = default)
        {
            if (owner.AncestorFailures.Contains((ancestorSha, descendantSha)))
            {
                throw new DomainRuleException(DomainRule.GitAccessFailed, "The commit is unknown.");
            }

            return Task.FromResult(
                ancestorSha == descendantSha || owner.Ancestors.Contains((ancestorSha, descendantSha)));
        }

        /// <inheritdoc />
        public async Task<GitChangeSet> ListChangesAsync(
            string? baselineSha,
            string headSha,
            CancellationToken cancellationToken = default)
        {
            owner.ListCalls++;
            var hook = owner.OnNextListChanges;
            owner.OnNextListChanges = null;
            if (hook is not null)
            {
                await hook();
            }

            var set = owner.Changes[(baselineSha ?? string.Empty, headSha)];
            return owner.TrackChangeSets ? owner.Track(set) : set;
        }

        /// <inheritdoc />
        public Task<long> GetBlobSizeAsync(string blobSha, CancellationToken cancellationToken = default)
        {
            return Task.FromResult((long)owner.Blobs[blobSha].Length);
        }

        /// <inheritdoc />
        public Task<byte[]> ReadBlobAsync(string blobSha, long maxBytes, CancellationToken cancellationToken = default)
        {
            var bytes = owner.Blobs[blobSha];
            owner.BlobReads++;
            return bytes.Length > maxBytes
                ? throw new DomainRuleException(DomainRule.GitAccessFailed, "The file is too large.")
                : Task.FromResult(bytes);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
