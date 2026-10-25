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

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.ContentSources;

/// <summary>
///     Reads registered git repositories through a local bare mirror and the installed git program.
/// </summary>
/// <remarks>
///     <para>
///         Each repository has one bare mirror folder, <c>repo-{id}</c>, under the cache folder. Opening a session
///         clones the mirror when it is missing (or when the registered location changed) and otherwise fetches
///         into it, so a session always sees the current state of the remote. A failed clone leaves no folder. The
///         mirror is never a working tree: files are read as raw blobs, with no filters, line-ending conversion or
///         large-file substitution, so the bytes are exactly what the repository stores.
///     </para>
///     <para>
///         Sessions on the same repository run one after another (a semaphore held until the session is
///         disposed); sessions on different repositories run in parallel. Every revision and blob identifier is
///         validated before it is used and is passed after <c>--end-of-options</c> or as a bare hexadecimal
///         value, never through a shell. The class is thread-safe.
///     </para>
/// </remarks>
internal sealed class GitSource : IGitRepositoryProvider
{
    /// <summary>
    ///     The mode of a symbolic link in a git tree.
    /// </summary>
    private const string SymlinkMode = "120000";

    /// <summary>
    ///     The mode of a submodule entry in a git tree.
    /// </summary>
    private const string SubmoduleMode = "160000";

    /// <summary>
    ///     The most output a change listing may have, in bytes.
    /// </summary>
    private const long MaxListingBytes = 64L * 1024 * 1024;

    /// <summary>
    ///     Matches a lowercase hexadecimal object identifier (SHA-1 or SHA-256).
    /// </summary>
    private static readonly Regex ObjectIdPattern =
        new("^([0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    ///     The git runner.
    /// </summary>
    private readonly GitRunner _runner;

    /// <summary>
    ///     The settings.
    /// </summary>
    private readonly GitOptions _options;

    /// <summary>
    ///     The per-repository gates, keyed by repository identity.
    /// </summary>
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _gates = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="GitSource"/> class.
    /// </summary>
    /// <param name="runner">The git runner. Must not be null.</param>
    /// <param name="options">The git settings. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public GitSource(GitRunner runner, GitOptions options)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(options);
        _runner = runner;
        _options = options;
    }

    /// <inheritdoc />
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.GitUnavailable"/> when the cache folder cannot be written or its path is malformed or too long; the message
    ///     names the setting but not the path, and the repository gate is released.
    /// </exception>
    public async Task<IGitRepository> OpenAsync(RepositoryAccess access, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (string.IsNullOrWhiteSpace(_options.CachePath))
        {
            throw new InvalidOperationException("The git cache folder is not configured.");
        }

        var gate = _gates.GetOrAdd(access.RepositoryId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var folder = Path.Combine(Path.GetFullPath(_options.CachePath), string.Create(CultureInfo.InvariantCulture, $"repo-{access.RepositoryId}"));
            await EnsureMirrorAsync(access, folder, cancellationToken);
            return new Session(_runner, access, folder, gate);
        }
        catch (Exception ex) when (GitRunner.IsCacheFolderError(ex))
        {
            // The raw message names server paths; the exception is deliberately not chained
            gate.Release();
            throw GitRunner.CacheFolderUnwritable();
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    /// <summary>
    ///     Makes the mirror folder current: clones it when missing or pointing elsewhere, otherwise fetches.
    /// </summary>
    /// <param name="access">The repository.</param>
    /// <param name="folder">The mirror folder.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the mirror is current.</returns>
    private async Task EnsureMirrorAsync(RepositoryAccess access, string folder, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
        if (Directory.Exists(folder))
        {
            var current = await _runner.RunAsync(
                ["--git-dir", folder, "config", "--get", "remote.origin.url"],
                null,
                4096,
                cancellationToken);
            var url = Encoding.UTF8.GetString(current.Output).Trim();
            if (current.ExitCode == 0 && string.Equals(url, access.Location, StringComparison.Ordinal))
            {
                await _runner.RunCheckedAsync(
                    ["--git-dir", folder, "fetch", "--prune", "--force", "origin"],
                    access,
                    GitRunner.DefaultMaxOutputBytes,
                    cancellationToken);
                return;
            }

            // The registered location changed (or the mirror is damaged): start again
            DeleteFolder(folder);
        }

        var temporary = folder + ".tmp";
        DeleteFolder(temporary);
        try
        {
            await _runner.RunCheckedAsync(
                ["clone", "--mirror", "--", access.Location, temporary],
                access,
                GitRunner.DefaultMaxOutputBytes,
                cancellationToken);
            Directory.Move(temporary, folder);
        }
        catch
        {
            DeleteFolder(temporary);
            throw;
        }
    }

    /// <summary>
    ///     Deletes a folder if it exists, clearing read-only attributes first (git writes read-only pack files).
    /// </summary>
    /// <param name="folder">The folder.</param>
    private static void DeleteFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(folder, recursive: true);
    }

    /// <summary>
    ///     A session on one mirror; it owns the repository gate until it is disposed.
    /// </summary>
    private sealed class Session : IGitRepository
    {
        /// <summary>
        ///     Checks that a text is a lowercase hexadecimal object identifier.
        /// </summary>
        /// <param name="value">The text.</param>
        /// <returns><c>true</c> when it is 40 or 64 lowercase hexadecimal characters.</returns>
        private static bool IsObjectId(string? value) => value is not null && ObjectIdPattern.IsMatch(value);
        /// <summary>
        ///     The git runner.
        /// </summary>
        private readonly GitRunner _runner;

        /// <summary>
        ///     The repository.
        /// </summary>
        private readonly RepositoryAccess _access;

        /// <summary>
        ///     The mirror folder.
        /// </summary>
        private readonly string _folder;

        /// <summary>
        ///     The gate released on disposal.
        /// </summary>
        private readonly SemaphoreSlim _gate;

        /// <summary>
        ///     Whether the session was disposed (0 or 1).
        /// </summary>
        private int _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Session"/> class.
        /// </summary>
        /// <param name="runner">The git runner.</param>
        /// <param name="access">The repository.</param>
        /// <param name="folder">The mirror folder.</param>
        /// <param name="gate">The gate to release on disposal; already held.</param>
        public Session(GitRunner runner, RepositoryAccess access, string folder, SemaphoreSlim gate)
        {
            _runner = runner;

            // Reading a local mirror needs no credentials, so the token stays out of these git processes
            _access = access with { Username = null, Token = null };
            _folder = folder;
            _gate = gate;
        }

        /// <inheritdoc />
        public async Task<string> ResolveRevisionAsync(string revision, CancellationToken cancellationToken = default)
        {
            RevisionValidator.Validate(revision);
            var result = await _runner.RunAsync(
                ["--git-dir", _folder, "rev-parse", "--verify", "--end-of-options", revision + "^{commit}"],
                _access,
                4096,
                cancellationToken);
            var sha = Encoding.UTF8.GetString(result.Output).Trim();
            if (result.ExitCode != 0 || !IsObjectId(sha))
            {
                throw new DomainRuleException(
                    DomainRule.RevisionNotFound,
                    $"The revision '{revision}' does not exist in repository '{_access.Name}'.");
            }

            return sha;
        }

        /// <inheritdoc />
        public async Task<GitChangeSet> ListChangesAsync(
            string? baselineSha,
            string headSha,
            CancellationToken cancellationToken = default)
        {
            RequireObjectId(headSha);
            var changes = new List<GitChange>();
            var skipped = new List<SkippedEntry>();
            if (baselineSha is null)
            {
                var output = await _runner.RunCheckedAsync(
                    ["--git-dir", _folder, "ls-tree", "-r", "-z", "--full-tree", headSha],
                    _access,
                    MaxListingBytes,
                    cancellationToken);
                ParseTree(Encoding.UTF8.GetString(output), changes, skipped);
            }
            else
            {
                RequireObjectId(baselineSha);
                var output = await _runner.RunCheckedAsync(
                    ["--git-dir", _folder, "diff-tree", "-r", "-z", "--raw", "--no-renames", "--no-abbrev", baselineSha, headSha, "--"],
                    _access,
                    MaxListingBytes,
                    cancellationToken);
                ParseDiff(Encoding.UTF8.GetString(output), changes, skipped);
            }

            return new GitChangeSet(
                changes.OrderBy(c => c.Path, StringComparer.Ordinal).ToList(),
                skipped.OrderBy(s => s.Path, StringComparer.Ordinal).ToList());
        }

        /// <inheritdoc />
        public async Task<long> GetBlobSizeAsync(string blobSha, CancellationToken cancellationToken = default)
        {
            RequireObjectId(blobSha);
            var output = await _runner.RunCheckedAsync(
                ["--git-dir", _folder, "cat-file", "-s", blobSha],
                _access,
                4096,
                cancellationToken);
            if (!long.TryParse(Encoding.UTF8.GetString(output).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var size))
            {
                throw new DomainRuleException(DomainRule.GitAccessFailed, $"Repository '{_access.Name}' returned an unexpected size.");
            }

            return size;
        }

        /// <inheritdoc />
        public async Task<byte[]> ReadBlobAsync(string blobSha, long maxBytes, CancellationToken cancellationToken = default)
        {
            var size = await GetBlobSizeAsync(blobSha, cancellationToken);
            if (size > maxBytes)
            {
                throw new DomainRuleException(
                    DomainRule.GitAccessFailed,
                    $"A file in repository '{_access.Name}' is larger than the allowed {maxBytes} bytes.");
            }

            return await _runner.RunCheckedAsync(
                ["--git-dir", _folder, "cat-file", "blob", blobSha],
                _access,
                maxBytes,
                cancellationToken);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _gate.Release();
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>
        ///     Checks that a value is an object identifier, so it can never be read as an option.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <exception cref="ArgumentException">Thrown when it is not 40 or 64 lowercase hexadecimal characters.</exception>
        private static void RequireObjectId(string value)
        {
            if (!IsObjectId(value))
            {
                throw new ArgumentException("An object identifier must be lowercase hexadecimal.", nameof(value));
            }
        }

        /// <summary>
        ///     Reads the output of <c>git ls-tree -r -z</c> as added files.
        /// </summary>
        /// <param name="text">The output.</param>
        /// <param name="changes">Receives the files.</param>
        /// <param name="skipped">Receives the skipped entries.</param>
        private static void ParseTree(string text, List<GitChange> changes, List<SkippedEntry> skipped)
        {
            foreach (var record in text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                // <mode> SP <type> SP <sha> TAB <path>
                var tab = record.IndexOf('\t', StringComparison.Ordinal);
                var meta = tab < 0 ? [] : record[..tab].Split(' ');
                if (meta.Length != 3)
                {
                    continue;
                }

                var path = record[(tab + 1)..];
                if (SkipReason(path, meta[0]) is { } reason)
                {
                    skipped.Add(new SkippedEntry(path, reason));
                }
                else
                {
                    changes.Add(new GitChange(path, ChangeKind.Added, null, meta[2]));
                }
            }
        }

        /// <summary>
        ///     Reads the output of <c>git diff-tree -r -z --raw</c>.
        /// </summary>
        /// <param name="text">The output.</param>
        /// <param name="changes">Receives the changed files.</param>
        /// <param name="skipped">Receives the skipped entries.</param>
        private static void ParseDiff(string text, List<GitChange> changes, List<SkippedEntry> skipped)
        {
            var tokens = text.Split('\0');
            for (var i = 0; i + 1 < tokens.Length; i += 2)
            {
                // :<srcmode> SP <dstmode> SP <srcsha> SP <dstsha> SP <status>, then the path
                var meta = tokens[i].TrimStart(':').Split(' ');
                var path = tokens[i + 1];
                if (meta.Length != 5)
                {
                    continue;
                }

                var reason = SkipReason(path, meta[0]) ?? SkipReason(path, meta[1]);
                if (reason is not null)
                {
                    skipped.Add(new SkippedEntry(path, reason));
                    continue;
                }

                switch (meta[4])
                {
                    case "A":
                        changes.Add(new GitChange(path, ChangeKind.Added, null, meta[3]));
                        break;
                    case "D":
                        changes.Add(new GitChange(path, ChangeKind.Deleted, meta[2], null));
                        break;
                    default:
                        // A mode-only change keeps the same blob and is not a change of content
                        if (!string.Equals(meta[2], meta[3], StringComparison.Ordinal))
                        {
                            changes.Add(new GitChange(path, ChangeKind.Modified, meta[2], meta[3]));
                        }

                        break;
                }
            }
        }

        /// <summary>
        ///     Decides whether an entry is left out, and why.
        /// </summary>
        /// <param name="path">The repository path.</param>
        /// <param name="mode">The file mode of the entry (<c>000000</c> for none).</param>
        /// <returns>The reason, or <c>null</c> when the entry is a regular file with a usable path.</returns>
        private static string? SkipReason(string path, string mode)
        {
            if (mode == SymlinkMode)
            {
                return "symbolic links are not reviewed";
            }

            if (mode == SubmoduleMode)
            {
                return "submodules are not reviewed";
            }

            if (path.Length > ReviewLimits.MaxPathLength)
            {
                return $"the path is longer than {ReviewLimits.MaxPathLength} characters";
            }

            if (path.Any(c => char.IsControl(c) || c == '\uFFFD'))
            {
                return "the path has control or invalid characters";
            }

            return null;
        }
    }
}
