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

using System.Diagnostics;
using System.Text.Json;

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     The lifecycle state recorded in a session file.
/// </summary>
internal enum SessionStatus
{
    /// <summary>
    ///     The daemon is starting the server and the browser; commands cannot be accepted yet.
    /// </summary>
    Starting,

    /// <summary>
    ///     The server and browser are up and the control channel accepts commands.
    /// </summary>
    Ready,

    /// <summary>
    ///     Start-up failed; <see cref="SessionInfo.Error"/> says why.
    /// </summary>
    Failed
}

/// <summary>
///     The persisted description of a Puppet session: how to reach its daemon and which processes it owns.
/// </summary>
/// <param name="SessionId">The session id (also the file name).</param>
/// <param name="Status">The lifecycle state.</param>
/// <param name="ControlPort">The loopback TCP port of the daemon's control channel.</param>
/// <param name="ControlToken">The shared secret the daemon requires on every request.</param>
/// <param name="ServerUrl">The base URL of the ReviewCenter server the session drives; empty until ready.</param>
/// <param name="DaemonPid">The process id of the daemon.</param>
/// <param name="DaemonStartTimeUtc">The daemon's start time; with the pid it identifies the process uniquely.</param>
/// <param name="ServerPid">The process id of the server the daemon started; 0 until started.</param>
/// <param name="ServerStartTimeUtc">The server's start time, used with <paramref name="ServerPid"/>.</param>
/// <param name="BrowserPid">The process id of the Chromium browser process Playwright launched; 0 until launched or when unknown.</param>
/// <param name="BrowserStartTimeUtc">The browser's start time, used with <paramref name="BrowserPid"/>.</param>
/// <param name="Error">The failure reason when <paramref name="Status"/> is <see cref="SessionStatus.Failed"/>.</param>
internal sealed record SessionInfo(
    string SessionId,
    SessionStatus Status,
    int ControlPort,
    string ControlToken,
    string ServerUrl,
    int DaemonPid,
    DateTime DaemonStartTimeUtc,
    int ServerPid,
    DateTime ServerStartTimeUtc,
    int BrowserPid,
    DateTime BrowserStartTimeUtc,
    string? Error);

/// <summary>
///     Identifies one process uniquely: a process id alone can be recycled, the pair with the start time cannot.
/// </summary>
/// <param name="ProcessId">The process id.</param>
/// <param name="StartTimeUtc">The process start time, in UTC.</param>
internal readonly record struct ProcessIdentity(int ProcessId, DateTime StartTimeUtc);

/// <summary>
///     The outcome of looking a session up.
/// </summary>
internal enum SessionLookup
{
    /// <summary>
    ///     The session exists and its daemon is running.
    /// </summary>
    Live,

    /// <summary>
    ///     No session file exists for the id.
    /// </summary>
    Unknown,

    /// <summary>
    ///     A session file existed but its daemon is gone; the file (and any orphaned server) was cleaned up.
    /// </summary>
    Stale
}

/// <summary>
///     The outcome of a conditional delete of a session file (see <see cref="SessionStore.DeleteIfSame"/>).
/// </summary>
internal enum DeleteOutcome
{
    /// <summary>
    ///     The file was the expected session and was removed, or it was already gone.
    /// </summary>
    Deleted,

    /// <summary>
    ///     The file now describes a different session (a newer <c>start</c> replaced it) and was left alone.
    /// </summary>
    Replaced,

    /// <summary>
    ///     A <c>start</c> holds the start lock, or the file could not be read; nothing was deleted.
    /// </summary>
    Busy
}

/// <summary>
///     The classified result of reading a session file.
/// </summary>
internal enum ReadOutcome
{
    /// <summary>
    ///     The file was read and parsed.
    /// </summary>
    Read,

    /// <summary>
    ///     The file does not exist.
    /// </summary>
    Missing,

    /// <summary>
    ///     The file exists but is not a valid session record.
    /// </summary>
    Corrupt,

    /// <summary>
    ///     The file could not be read because it stayed locked or access was refused; its state is unknown.
    /// </summary>
    Unavailable
}

/// <summary>
///     Reads, writes, and cleans up the per-session files under the system temporary directory.
/// </summary>
/// <remarks>
///     One small JSON file per session is the only shared state between the short-lived client
///     commands and the long-lived daemon. A file whose daemon has died (crash, reboot, killed
///     terminal) would otherwise make every later command fail confusingly, so every lookup
///     verifies that the daemon process is genuinely alive and removes the file when it is not.
///     Process identity is the pair (pid, start time) so that a recycled pid is not mistaken for the
///     old daemon. Writes go through a temporary file and an atomic move so a reader never sees a
///     half-written file. The store holds no state besides its directory and is safe to use from
///     several processes.
///     <para>
///     A session file contains the control token that authorizes commands to the daemon, so it must
///     not be readable by other users. On Unix the directory is created with mode 0700 and every
///     file with mode 0600, both set at creation time so there is never a moment when they are more
///     widely accessible (an existing directory with wider permissions is tightened). On Windows
///     the default directory lives under the per-user temporary directory, which is already
///     accessible only to that user, so no extra access control is applied.
///     </para>
/// </remarks>
internal sealed class SessionStore
{
    /// <summary>
    ///     The Unix mode of the session directory: accessible to the owner only.
    /// </summary>
    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    ///     The Unix mode of a session file: readable and writable by the owner only.
    /// </summary>
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    ///     How far a process's actual start time may differ from the recorded one and still count as the
    ///     same process (clock resolution differs between platforms and reads); a recycled pid starts much later.
    /// </summary>
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     The extension of the start lock file that sits beside the session file of the same id.
    /// </summary>
    private const string LockExtension = ".lock";

    /// <summary>
    ///     How many times <see cref="TryReserve"/> tries to take the lock when it keeps finding stale ones.
    /// </summary>
    private const int LockAttempts = 2;

    /// <summary>
    ///     How long a lock file whose owner cannot be read is assumed to be still in the middle of being
    ///     written before it counts as stale.
    /// </summary>
    private static readonly TimeSpan LockGracePeriod = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How many times <see cref="Delete"/> tries to remove a file that another party is deleting at the same moment.
    /// </summary>
    private const int DeleteAttempts = 5;

    /// <summary>
    ///     How long <see cref="Delete"/> waits between two attempts.
    /// </summary>
    private static readonly TimeSpan DeleteRetryDelay = TimeSpan.FromMilliseconds(20);

    /// <summary>
    ///     How many times a session file read is tried when the file is transiently locked or being replaced.
    /// </summary>
    private const int ReadAttempts = 20;

    /// <summary>
    ///     How long a read waits between two attempts (about half a second in total).
    /// </summary>
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(25);

    /// <summary>
    ///     How many times the replacement of a session file is tried when it is transiently refused.
    /// </summary>
    private const int ReplaceAttempts = 20;

    /// <summary>
    ///     How long a replacement waits between two attempts.
    /// </summary>
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(25);

    /// <summary>
    ///     How many times <see cref="Lookup"/> re-examines a session file that a concurrent <c>start</c>
    ///     replaced while the lookup was cleaning up the previous record.
    /// </summary>
    private const int LookupAttempts = 3;

    /// <summary>
    ///     JSON options shared by all reads and writes.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>
    ///     Decides whether the process identified by a pid and start time is still running.
    /// </summary>
    private readonly Func<int, DateTime, bool> _isProcessAlive;

    /// <summary>
    ///     Terminates the process identified by a pid and start time (used for orphaned servers).
    /// </summary>
    private readonly Action<int, DateTime> _killProcess;

    /// <summary>
    ///     Waits between two attempts of a read or a replacement that is transiently refused.
    /// </summary>
    private readonly Action<TimeSpan> _sleep;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SessionStore"/> class.
    /// </summary>
    /// <param name="directory">
    ///     The directory holding session files; <c>null</c> selects <c>reviewcenter-puppet</c> under
    ///     the system temporary directory.
    /// </param>
    /// <param name="isProcessAlive">Process liveness check; <c>null</c> uses <see cref="IsProcessAlive"/>.</param>
    /// <param name="killProcess">Process termination; <c>null</c> uses <see cref="KillProcess"/>.</param>
    /// <param name="sleep">
    ///     Wait between retry attempts of a read or a replacement; <c>null</c> sleeps the calling thread.
    ///     Tests inject it to release a file lock deterministically instead of racing a timer.
    /// </param>
    public SessionStore(
        string? directory = null,
        Func<int, DateTime, bool>? isProcessAlive = null,
        Action<int, DateTime>? killProcess = null,
        Action<TimeSpan>? sleep = null)
    {
        Directory = directory ?? Path.Combine(Path.GetTempPath(), "reviewcenter-puppet");
        _isProcessAlive = isProcessAlive ?? IsProcessAlive;
        _killProcess = killProcess ?? KillProcess;
        _sleep = sleep ?? Thread.Sleep;
    }

    /// <summary>
    ///     Gets the directory that holds the session files.
    /// </summary>
    public string Directory { get; }

    /// <summary>
    ///     Gets the path of the session file for an id.
    /// </summary>
    /// <param name="sessionId">A valid session id.</param>
    /// <returns>The full file path.</returns>
    /// <exception cref="ArgumentException">Thrown when the id is not valid (path traversal guard).</exception>
    public string GetPath(string sessionId)
    {
        if (!CommandLine.IsValidSessionId(sessionId))
        {
            throw new ArgumentException($"'{sessionId}' is not a valid session id.", nameof(sessionId));
        }

        return Path.Combine(Directory, sessionId + ".json");
    }

    /// <summary>
    ///     Writes a session file, replacing any existing one atomically.
    /// </summary>
    /// <param name="info">The session to persist.</param>
    /// <exception cref="PuppetException">Thrown when the session directory cannot be made private to the user.</exception>
    public void Write(SessionInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        EnsureDirectory();
        var path = GetPath(info.SessionId);

        // Write beside the target and move into place so readers see all or nothing
        var temp = WriteTemporaryFile(path, info);
        try
        {
            ReplaceWithRetry(temp, path);
        }
        catch
        {
            // The temporary file holds the control token: never leave it behind
            DeleteQuietly(temp);
            throw;
        }
    }

    /// <summary>
    ///     Moves a temporary file over the target, retrying briefly when the replacement is transiently refused.
    /// </summary>
    /// <param name="temp">The temporary file.</param>
    /// <param name="path">The target path.</param>
    private void ReplaceWithRetry(string temp, string path)
    {
        for (var attempt = 1; attempt < ReplaceAttempts; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _sleep(ReplaceRetryDelay);
            }
        }

        // The last attempt is not caught: a persistent failure propagates to the caller
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    ///     Deletes a file, ignoring any failure.
    /// </summary>
    /// <param name="path">The file to delete.</param>
    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the original failure is the one worth reporting
        }
    }

    /// <summary>
    ///     Takes the start lock for a session id, failing when another <c>start</c> already holds it.
    /// </summary>
    /// <remarks>
    ///     Two <c>start</c> commands for the same id must not both launch a daemon. The lock is a small
    ///     file created with <see cref="FileMode.CreateNew"/>, which every supported file system
    ///     performs atomically: of several simultaneous callers exactly one succeeds. The file names the
    ///     owning process (id and start time), so a lock left behind by a <c>start</c> that was killed is
    ///     recognized as stale and taken over; a lock whose content cannot be read yet is treated as held
    ///     until it is older than the lock grace period. The holder must call <see cref="Release"/> when
    ///     its start has finished, whatever the outcome, and should look the session up again after taking
    ///     the lock, because another start may have completed between its first look and the lock.
    ///     Taking over a stale lock is best effort: it is atomic against other takeovers of the same stale
    ///     file, but two callers racing over one stale lock while a third takes it fresh can, in theory,
    ///     both succeed; that needs a killed <c>start</c> and three simultaneous callers.
    /// </remarks>
    /// <param name="sessionId">The session id to lock.</param>
    /// <returns><c>true</c> when this call took the lock; <c>false</c> when another start holds it.</returns>
    /// <exception cref="PuppetException">Thrown when the session directory cannot be made private to the user.</exception>
    public bool TryReserve(string sessionId)
    {
        EnsureDirectory();
        var path = GetLockPath(sessionId);

        // The calling process owns the lock; its identity lets a later caller tell a live lock from a leftover
        using var self = Process.GetCurrentProcess();
        var owner = new ProcessIdentity(self.Id, self.StartTime.ToUniversalTime());
        for (var attempt = 0; attempt < LockAttempts; attempt++)
        {
            if (TryCreateLock(path, owner))
            {
                return true;
            }

            if (!TryReclaimStaleLock(path))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    ///     Releases the start lock for a session id; does nothing when it is not held.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    public void Release(string sessionId)
    {
        try
        {
            File.Delete(GetLockPath(sessionId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a lock that cannot be removed is found stale later, once its owner has exited
        }
    }

    /// <summary>
    ///     Gets the path of the start lock file for an id.
    /// </summary>
    /// <param name="sessionId">A valid session id.</param>
    /// <returns>The full file path.</returns>
    private string GetLockPath(string sessionId)
    {
        // GetPath validates the id (path traversal guard)
        return Path.ChangeExtension(GetPath(sessionId), LockExtension);
    }

    /// <summary>
    ///     Creates the lock file exclusively and records its owner, owner-only on Unix.
    /// </summary>
    /// <param name="path">The lock file path.</param>
    /// <param name="owner">The process that takes the lock.</param>
    /// <returns><c>true</c> when the file was created; <c>false</c> when it already exists.</returns>
    private static bool TryCreateLock(string path, ProcessIdentity owner)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = PrivateFileMode;
        }

        try
        {
            using var stream = new FileStream(path, options);
            JsonSerializer.Serialize(stream, owner, JsonOptions);
            return true;
        }
        catch (IOException ex) when (ex is not DirectoryNotFoundException)
        {
            // The file exists (held by another start), or it existed a moment ago and its holder has already
            // released it. Either way the caller looks at the file again and tries once more
            return false;
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            // Windows refuses to create a file whose deletion is still pending, which is what a lock that was
            // just released looks like while another caller still has it open. The directory is private to this
            // user, so this is not a permission problem: treat the lock as held and let the caller look again
            return false;
        }
    }

    /// <summary>
    ///     Removes a lock file whose owner is gone, so that the caller can try to take the lock again.
    /// </summary>
    /// <param name="path">The lock file path.</param>
    /// <returns><c>true</c> when the lock is free to be taken again; <c>false</c> when a live start holds it.</returns>
    private bool TryReclaimStaleLock(string path)
    {
        ProcessIdentity? owner = null;
        try
        {
            owner = JsonSerializer.Deserialize<ProcessIdentity>(File.ReadAllText(path), JsonOptions);
        }
        catch (FileNotFoundException)
        {
            // Released in the meantime: free
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Not readable yet (the owner may still be writing it): decided by age below
        }

        bool stale;
        if (owner is { ProcessId: > 0 } known)
        {
            stale = !_isProcessAlive(known.ProcessId, known.StartTimeUtc);
        }
        else
        {
            stale = DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > LockGracePeriod;
        }

        if (!stale)
        {
            return false;
        }

        // Rename rather than delete: of several callers that found the same stale file only one rename succeeds
        var graveyard = path + "." + Guid.NewGuid().ToString("N") + ".stale";
        try
        {
            File.Move(path, graveyard);
            File.Delete(graveyard);
            return true;
        }
        catch (FileNotFoundException)
        {
            // Someone else removed it first
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Creates the session directory if needed, private to the current user on Unix.
    /// </summary>
    /// <exception cref="PuppetException">Thrown when an existing directory cannot be made private to the user.</exception>
    private void EnsureDirectory()
    {
        // Windows: the per-user temporary directory is already private to the user
        if (OperatingSystem.IsWindows())
        {
            System.IO.Directory.CreateDirectory(Directory);
            return;
        }

        // Unix: the mode is applied at creation so there is no window of wider access
        System.IO.Directory.CreateDirectory(Directory, PrivateDirectoryMode);
        try
        {
            // A directory that already existed (for example from an older version) keeps its old mode; tighten it
            if (File.GetUnixFileMode(Directory) != PrivateDirectoryMode)
            {
                File.SetUnixFileMode(Directory, PrivateDirectoryMode);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new PuppetException(
                $"The session directory '{Directory}' cannot be made private to the current user: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Writes a session record to a new temporary file next to its final path.
    /// </summary>
    /// <remarks>
    ///     The file is created exclusively (<see cref="FileMode.CreateNew"/>) and, on Unix, with the
    ///     owner-only mode already applied by the create call itself, because the content includes the
    ///     control token.
    /// </remarks>
    /// <param name="path">The final session file path.</param>
    /// <param name="info">The record to write.</param>
    /// <returns>The path of the temporary file.</returns>
    private static string WriteTemporaryFile(string path, SessionInfo info)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = PrivateFileMode;
        }

        try
        {
            using var stream = new FileStream(temp, options);
            JsonSerializer.Serialize(stream, info, JsonOptions);
            return temp;
        }
        catch
        {
            // A partly written file would hold (part of) the control token
            DeleteQuietly(temp);
            throw;
        }
    }


    /// <summary>
    ///     Reads a session file without any liveness check.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <returns>
    ///     The session, or <c>null</c> when the file is missing, corrupt, or still locked after a brief
    ///     retry; callers that must tell those apart (and must not clean up on an indeterminate read)
    ///     use <see cref="Lookup"/>.
    /// </returns>
    public SessionInfo? TryRead(string sessionId)
    {
        var (_, info) = ReadWithRetry(GetPath(sessionId));
        return info;
    }

    /// <summary>
    ///     Reads and parses a session file, retrying briefly while the file is transiently unavailable.
    /// </summary>
    /// <remarks>
    ///     The file is opened with full sharing (including delete), so the daemon's atomic replacement
    ///     can proceed while a read is in flight. A sharing or access error is nevertheless possible for
    ///     an instant while the replacement happens (notably on Windows), so such an error is retried
    ///     and, if it persists, reported as <see cref="ReadOutcome.Unavailable"/> - never as missing or
    ///     corrupt, which are the only outcomes that justify cleaning a session up.
    /// </remarks>
    /// <param name="path">The session file path.</param>
    /// <returns>The outcome, and the session when the outcome is <see cref="ReadOutcome.Read"/>.</returns>
    private (ReadOutcome Outcome, SessionInfo? Info) ReadWithRetry(string path)
    {
        for (var attempt = 1; attempt <= ReadAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var info = JsonSerializer.Deserialize<SessionInfo>(stream, JsonOptions);
                return info is null ? (ReadOutcome.Corrupt, null) : (ReadOutcome.Read, info);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return (ReadOutcome.Missing, null);
            }
            catch (JsonException)
            {
                // A corrupt file is reported as such; the caller cleans it up as stale
                return (ReadOutcome.Corrupt, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file is being replaced right now or is locked: wait a moment and look again
                if (attempt < ReadAttempts)
                {
                    _sleep(ReadRetryDelay);
                }
            }
        }

        return (ReadOutcome.Unavailable, null);
    }

    /// <summary>
    ///     Deletes a session file if it exists.
    /// </summary>
    /// <remarks>
    ///     Several parties can delete the same file at about the same moment (the daemon finishing its
    ///     cleanup, a <c>stop</c> command, or a <c>start</c> that is aborting). On Windows a delete that
    ///     races another delete can fail with an access error although the file is going away, so a
    ///     failure is retried briefly and is ignored once the file is gone.
    /// </remarks>
    /// <param name="sessionId">The session id.</param>
    public void Delete(string sessionId)
    {
        var path = GetPath(sessionId);
        for (var attempt = 1; attempt <= DeleteAttempts; attempt++)
        {
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == DeleteAttempts)
                {
                    throw;
                }

                Thread.Sleep(DeleteRetryDelay);
            }
        }
    }

    /// <summary>
    ///     Deletes a session file only if it still describes the session the caller examined.
    /// </summary>
    /// <remarks>
    ///     A caller that read a session, decided it is finished (stopped or stale), and then deletes the
    ///     file by name can remove a newer session that a concurrent <c>start</c> published in between.
    ///     This method closes that window: it takes the start lock (so no <c>start</c> can publish while
    ///     it checks and deletes), re-reads the file, and deletes it only when the daemon identity and
    ///     control token still match. When a <c>start</c> already holds the lock the file is left alone:
    ///     that start looks the old session up itself and replaces or cleans it. A caller that already
    ///     holds the start lock (the <c>start</c> command itself) passes <paramref name="startLockHeld"/>.
    /// </remarks>
    /// <param name="sessionId">The session id.</param>
    /// <param name="expected">
    ///     The record that was examined; <c>null</c> when the examined file was corrupt (only a still
    ///     corrupt file is then deleted).
    /// </param>
    /// <param name="startLockHeld"><c>true</c> when the caller already owns the start lock for the id.</param>
    /// <returns>Whether the file was deleted, replaced by another session, or could not be examined safely.</returns>
    public DeleteOutcome DeleteIfSame(string sessionId, SessionInfo? expected, bool startLockHeld = false)
    {
        var reserved = false;
        if (!startLockHeld)
        {
            if (!TryReserve(sessionId))
            {
                return DeleteOutcome.Busy;
            }

            reserved = true;
        }

        try
        {
            var (outcome, current) = ReadWithRetry(GetPath(sessionId));
            if (outcome == ReadOutcome.Missing)
            {
                return DeleteOutcome.Deleted;
            }

            if (outcome == ReadOutcome.Unavailable)
            {
                return DeleteOutcome.Busy;
            }

            // A readable record must be the one examined; a corrupt file only matches a corrupt examination
            var same = current is null ? expected is null : expected is not null && IsSameSession(current, expected);
            if (!same)
            {
                return DeleteOutcome.Replaced;
            }

            Delete(sessionId);
            return DeleteOutcome.Deleted;
        }
        finally
        {
            if (reserved)
            {
                Release(sessionId);
            }
        }
    }

    /// <summary>
    ///     Decides whether two records describe the same session instance: the same daemon process
    ///     (id and start time) holding the same control token.
    /// </summary>
    /// <param name="first">One record.</param>
    /// <param name="second">The other record.</param>
    /// <returns><c>true</c> when both records belong to the same daemon.</returns>
    public static bool IsSameSession(SessionInfo first, SessionInfo second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return first.SessionId == second.SessionId &&
               first.ControlToken == second.ControlToken &&
               first.DaemonPid == second.DaemonPid &&
               first.DaemonStartTimeUtc == second.DaemonStartTimeUtc;
    }

    /// <summary>
    ///     Lists the ids of all session files, sorted.
    /// </summary>
    /// <returns>The ids found; empty when the directory does not exist.</returns>
    public IReadOnlyList<string> ListIds()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        return System.IO.Directory.EnumerateFiles(Directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(CommandLine.IsValidSessionId)
            .Select(id => id!)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     Looks a session up and verifies that its daemon is alive, cleaning up when it is not.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="info">Receives the session when the result is <see cref="SessionLookup.Live"/>.</param>
    /// <param name="startLockHeld"><c>true</c> when the caller (a <c>start</c>) already owns the start lock for the id.</param>
    /// <returns>
    ///     <see cref="SessionLookup.Unknown"/> when no file exists; <see cref="SessionLookup.Stale"/>
    ///     when the file existed but the daemon is gone (a still-running orphaned server or browser is
    ///     stopped, and the file is deleted unless a concurrent <c>start</c> owns the id, in which case that
    ///     start replaces it) or is corrupt; otherwise <see cref="SessionLookup.Live"/>. A file that a
    ///     concurrent <c>start</c> replaced during the cleanup is never deleted; it is looked at again.
    /// </returns>
    /// <exception cref="PuppetException">
    ///     Thrown when the file stays unreadable (locked or access refused) after a brief retry, or keeps
    ///     being replaced. The state of the session is then unknown, so nothing is deleted or stopped.
    /// </exception>
    public SessionLookup Lookup(string sessionId, out SessionInfo? info, bool startLockHeld = false)
    {
        info = null;
        var path = GetPath(sessionId);
        for (var attempt = 1; attempt <= LookupAttempts; attempt++)
        {
            if (!File.Exists(path))
            {
                return SessionLookup.Unknown;
            }

            var (outcome, read) = ReadWithRetry(path);
            switch (outcome)
            {
                case ReadOutcome.Missing:
                    // The daemon (or a stop) removed the file between the existence check and the read
                    return SessionLookup.Unknown;
                case ReadOutcome.Unavailable:
                    // An indeterminate read proves nothing about the daemon: never clean up on it
                    throw new PuppetException(
                        $"The session file for '{sessionId}' is temporarily unreadable; nothing was changed. Try again.");
            }

            if (read is not null && _isProcessAlive(read.DaemonPid, read.DaemonStartTimeUtc))
            {
                info = read;
                return SessionLookup.Live;
            }

            // The daemon is gone (or the file is corrupt): stop anything it left running (processes are
            // identified by pid and start time, so a newer session's processes are never touched), then
            // remove the file only if it still is the record that was examined
            if (read is not null)
            {
                KillOrphans(read);
            }

            if (DeleteIfSame(sessionId, read, startLockHeld) != DeleteOutcome.Replaced)
            {
                return SessionLookup.Stale;
            }
        }

        throw new PuppetException(
            $"The session '{sessionId}' kept changing while it was being examined; nothing was changed. Try again.");
    }

    /// <summary>
    ///     Checks whether the daemon of a session is still running.
    /// </summary>
    /// <param name="info">The session record.</param>
    /// <returns><c>true</c> when the recorded daemon process is alive.</returns>
    public bool IsDaemonAlive(SessionInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return _isProcessAlive(info.DaemonPid, info.DaemonStartTimeUtc);
    }

    /// <summary>
    ///     Checks whether the process with the given identity is still running.
    /// </summary>
    /// <param name="process">The process id and start time.</param>
    /// <returns><c>true</c> when that exact process is alive.</returns>
    public bool IsAlive(ProcessIdentity process)
    {
        return _isProcessAlive(process.ProcessId, process.StartTimeUtc);
    }

    /// <summary>
    ///     Terminates the process with the given identity, if it is still that same process.
    /// </summary>
    /// <param name="process">The process id and start time; a recycled pid is never terminated.</param>
    public void Terminate(ProcessIdentity process)
    {
        _killProcess(process.ProcessId, process.StartTimeUtc);
    }

    /// <summary>
    ///     Kills the browser, the server, and the daemon of a session (and their child processes).
    /// </summary>
    /// <remarks>
    ///     The browser is terminated by its own recorded identity because once the daemon is dead the
    ///     browser is no longer part of its process tree and a tree kill would never reach it.
    /// </remarks>
    /// <param name="info">The session record.</param>
    public void Terminate(SessionInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        KillOrphans(info);
        _killProcess(info.DaemonPid, info.DaemonStartTimeUtc);
    }

    /// <summary>
    ///     Checks whether any process a session recorded (daemon, server, browser) is still running.
    /// </summary>
    /// <param name="info">The session record.</param>
    /// <returns><c>true</c> when at least one recorded process, identified by id and start time, is alive.</returns>
    public bool HasLiveProcesses(SessionInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return _isProcessAlive(info.DaemonPid, info.DaemonStartTimeUtc) ||
               (info.ServerPid > 0 && _isProcessAlive(info.ServerPid, info.ServerStartTimeUtc)) ||
               (info.BrowserPid > 0 && _isProcessAlive(info.BrowserPid, info.BrowserStartTimeUtc));
    }

    /// <summary>
    ///     Kills the browser and the server a session recorded, ignoring ones that never started.
    /// </summary>
    /// <param name="info">The session record.</param>
    private void KillOrphans(SessionInfo info)
    {
        if (info.BrowserPid > 0)
        {
            _killProcess(info.BrowserPid, info.BrowserStartTimeUtc);
        }

        if (info.ServerPid > 0)
        {
            _killProcess(info.ServerPid, info.ServerStartTimeUtc);
        }
    }

    /// <summary>
    ///     Checks whether a process with the given pid and start time is running.
    /// </summary>
    /// <param name="pid">The process id.</param>
    /// <param name="startTimeUtc">The recorded start time.</param>
    /// <returns>
    ///     <c>true</c> when a process with that pid exists, has not exited, and started at the recorded
    ///     time (within <see cref="StartTimeTolerance"/>), which rules out a recycled pid.
    /// </returns>
    public static bool IsProcessAlive(int pid, DateTime startTimeUtc)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return IsSameProcess(process, startTimeUtc);
        }
        catch (ArgumentException)
        {
            // No such process
            return false;
        }
        catch (InvalidOperationException)
        {
            // Exited while being inspected
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access denied: it is not a process this user started, so it is not ours
            return false;
        }
    }

    /// <summary>
    ///     Decides whether an acquired process is still running and started at the recorded time.
    /// </summary>
    /// <param name="process">The process object to inspect.</param>
    /// <param name="startTimeUtc">The recorded start time.</param>
    /// <returns><c>true</c> when the process has not exited and started within the tolerance of the recorded time.</returns>
    private static bool IsSameProcess(Process process, DateTime startTimeUtc)
    {
        return !process.HasExited &&
               (process.StartTime.ToUniversalTime() - startTimeUtc).Duration() < StartTimeTolerance;
    }

    /// <summary>
    ///     Terminates the process (and its children) identified by a pid and start time, if it is still the same process.
    /// </summary>
    /// <remarks>
    ///     The process is acquired once. Its operating system handle is opened before the start time is
    ///     checked, and the same object is then killed, so that where the platform ties a handle to one
    ///     process instance (Windows) the process that was checked is the process that is terminated and
    ///     a pid recycled in between cannot be hit. Where the platform offers no such handle, the check and
    ///     the kill are adjacent operations on one object, which leaves only a very small window.
    /// </remarks>
    /// <param name="pid">The process id.</param>
    /// <param name="startTimeUtc">The recorded start time, used to avoid killing a recycled pid.</param>
    public static void KillProcess(int pid, DateTime startTimeUtc)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            PinHandle(process);
            if (!IsSameProcess(process, startTimeUtc))
            {
                return;
            }

            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            // Already gone or not ours to stop; nothing more to do
        }
    }

    /// <summary>
    ///     Opens the operating system handle of a process object so that it keeps addressing that process.
    /// </summary>
    /// <param name="process">The process object.</param>
    private static void PinHandle(Process process)
    {
        try
        {
            _ = process.SafeHandle;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException
                                       or System.ComponentModel.Win32Exception)
        {
            // Exited, access refused, or not supported here: the identity check that follows decides what to do
        }
    }
}
