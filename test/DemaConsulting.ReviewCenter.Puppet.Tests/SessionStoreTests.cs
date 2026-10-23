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

using System.Runtime.Versioning;

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     Unit tests for the <see cref="SessionStore"/>.
/// </summary>
public class SessionStoreTests
{
    /// <summary>
    ///     Creates a session record for tests.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <param name="daemonPid">The daemon process id to record.</param>
    /// <param name="serverPid">The server process id to record.</param>
    /// <param name="browserPid">The browser process id to record.</param>
    /// <returns>A ready session record.</returns>
    private static SessionInfo CreateInfo(string id, int daemonPid = 1234, int serverPid = 5678, int browserPid = 9012)
    {
        return new SessionInfo(
            id, SessionStatus.Ready, 40000, "token", "http://127.0.0.1:5000", daemonPid,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), serverPid,
            new DateTime(2025, 1, 1, 0, 0, 1, DateTimeKind.Utc), browserPid,
            new DateTime(2025, 1, 1, 0, 0, 2, DateTimeKind.Utc), null);
    }

    /// <summary>
    ///     Test that a written session can be read back unchanged.
    /// </summary>
    [Fact]
    public void SessionStore_WriteRead_RoundTrip_ReturnsSameRecord()
    {
        // Arrange: a store in a temp folder and a record
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);
        var info = CreateInfo("demo");

        // Act: write and read it back
        store.Write(info);
        var read = store.TryRead("demo");

        // Assert: the record is identical, and stored as <id>.json
        Assert.Equal(info, read);
        Assert.True(File.Exists(Path.Combine(temp.Path, "demo.json")));
    }

    /// <summary>
    ///     Test that reading a session that does not exist returns nothing.
    /// </summary>
    [Fact]
    public void SessionStore_TryRead_MissingSession_ReturnsNull()
    {
        // Arrange: an empty store
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);

        // Act: read a session that was never written
        var read = store.TryRead("nope");

        // Assert: nothing to read
        Assert.Null(read);
    }

    /// <summary>
    ///     Test that a session id that could escape the folder is rejected.
    /// </summary>
    [Fact]
    public void SessionStore_GetPath_InvalidId_ThrowsArgumentException()
    {
        // Arrange: a store
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);

        // Act: ask for the path of a traversal id
        var exception = Record.Exception(() => store.GetPath("..\\x"));

        // Assert: it is refused
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>
    ///     Test that a corrupt file is treated as unreadable rather than crashing.
    /// </summary>
    [Fact]
    public void SessionStore_TryRead_CorruptFile_ReturnsNull()
    {
        // Arrange: a session file with garbage in it
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, "bad.json"), "{ not json");

        // Act: read it
        var read = store.TryRead("bad");

        // Assert: reading gives null
        Assert.Null(read);
    }

    /// <summary>
    ///     Test that a session with no file is reported unknown.
    /// </summary>
    [Fact]
    public void SessionStore_Lookup_NoFile_ReturnsUnknown()
    {
        // Arrange: an empty store
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path, (_, _) => true, (_, _) => { });

        // Act: look up a missing session
        var result = store.Lookup("ghost", out var info);

        // Assert: unknown, no info
        Assert.Equal(SessionLookup.Unknown, result);
        Assert.Null(info);
    }

    /// <summary>
    ///     Test that a session whose daemon is alive is reported live with its details.
    /// </summary>
    [Fact]
    public void SessionStore_Lookup_LiveDaemon_ReturnsLive()
    {
        // Arrange: a stored session and a liveness check that says the daemon runs
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path, (_, _) => true, (_, _) => { });
        store.Write(CreateInfo("live"));

        // Act: look it up
        var result = store.Lookup("live", out var info);

        // Assert: live with details, file kept
        Assert.Equal(SessionLookup.Live, result);
        Assert.Equal("http://127.0.0.1:5000", info!.ServerUrl);
        Assert.True(File.Exists(store.GetPath("live")));
    }

    /// <summary>
    ///     Test that a session whose daemon is dead is reported stale, its file deleted, and its orphan browser and
    ///     server killed.
    /// </summary>
    [Fact]
    public void SessionStore_ReadStale_DeadDaemon_CleansUpAndReports()
    {
        // Arrange: a stored session, a dead daemon, and a recording kill action
        using var temp = new TempDirectory();
        var killed = new List<int>();
        var store = new SessionStore(temp.Path, (_, _) => false, (pid, _) => killed.Add(pid));
        store.Write(CreateInfo("old", daemonPid: 111, serverPid: 222, browserPid: 333));

        // Act: look it up
        var result = store.Lookup("old", out var info);

        // Assert: stale, file gone, orphan browser and server killed (the dead daemon needs no kill)
        Assert.Equal(SessionLookup.Stale, result);
        Assert.Null(info);
        Assert.False(File.Exists(store.GetPath("old")));
        Assert.Equal([333, 222], killed);
    }

    /// <summary>
    ///     Test that an unreadable session file is also stale and is removed.
    /// </summary>
    [Fact]
    public void SessionStore_Lookup_CorruptFile_CleansUpAndReportsStale()
    {
        // Arrange: a corrupt session file
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path, (_, _) => true, (_, _) => { });
        var path = Path.Combine(temp.Path, "bad.json");
        File.WriteAllText(path, "garbage");

        // Act: look it up
        var result = store.Lookup("bad", out _);

        // Assert: stale and removed
        Assert.Equal(SessionLookup.Stale, result);
        Assert.False(File.Exists(path));
    }

    /// <summary>
    ///     Test that a file that is locked for a moment (as during an atomic replacement) is retried and the
    ///     session is still found live, not treated as stale.
    /// </summary>
    [Fact]
    public async Task SessionStore_Lookup_FileTemporarilyLocked_RetriesAndReportsLive()
    {
        // Arrange: a live session whose file is held exclusively for a short while
        using var temp = new TempDirectory();
        var killed = new List<int>();
        var store = new SessionStore(temp.Path, (_, _) => true, (pid, _) => killed.Add(pid));
        store.Write(CreateInfo("busy"));
        var hold = new FileStream(store.GetPath("busy"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(150, TestContext.Current.CancellationToken);
            await hold.DisposeAsync();
        }, TestContext.Current.CancellationToken);

        // Act: look it up while it is locked
        var result = store.Lookup("busy", out var info);
        await release;

        // Assert: live, nothing deleted, nothing killed
        Assert.Equal(SessionLookup.Live, result);
        Assert.NotNull(info);
        Assert.True(File.Exists(store.GetPath("busy")));
        Assert.Empty(killed);
    }

    /// <summary>
    ///     Test that a file that stays locked is reported as an error and the session is neither deleted nor
    ///     its processes killed (an indeterminate read must never trigger stale cleanup).
    /// </summary>
    [Fact]
    public void SessionStore_Lookup_FileStaysLocked_ThrowsAndKeepsSession()
    {
        // Arrange: a session whose file stays locked, and a liveness check that would say "dead"
        using var temp = new TempDirectory();
        var killed = new List<int>();
        var store = new SessionStore(temp.Path, (_, _) => false, (pid, _) => killed.Add(pid));
        store.Write(CreateInfo("stuck"));
        Exception? exception;
        using (new FileStream(store.GetPath("stuck"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // Act: look it up while it is locked
            exception = Record.Exception(() => store.Lookup("stuck", out _));
        }

        // Assert: reported as a puppet error, file intact and readable afterwards, nothing killed
        Assert.IsType<PuppetException>(exception);
        Assert.Contains("temporarily unreadable", exception.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(store.GetPath("stuck")));
        Assert.NotNull(store.TryRead("stuck"));
        Assert.Empty(killed);
    }

    /// <summary>
    ///     Test that a reader looking sessions up while a writer keeps replacing the file never sees it as
    ///     stale, missing, or failing.
    /// </summary>
    [Fact]
    public async Task SessionStore_WriteAndLookup_ConcurrentReplacement_NeverReportsStale()
    {
        // Arrange: a live session that is rewritten over and over while another thread looks it up
        using var temp = new TempDirectory();
        var killed = new List<int>();
        var store = new SessionStore(temp.Path, (_, _) => true, (pid, _) => killed.Add(pid));
        store.Write(CreateInfo("race"));
        var writing = true;
        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 300; i++)
            {
                store.Write(CreateInfo("race"));
            }

            writing = false;
        }, TestContext.Current.CancellationToken);

        // Act: look it up continuously until the writer is done
        var results = new List<SessionLookup>();
        do
        {
            results.Add(store.Lookup("race", out _));
        }
        while (Volatile.Read(ref writing));

        await writer;

        // Assert: every look-up found the live session; nothing was killed or deleted
        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.Equal(SessionLookup.Live, result));
        Assert.Empty(killed);
        Assert.True(File.Exists(store.GetPath("race")));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    /// <summary>
    ///     Test that a replacement refused for a moment is retried and succeeds.
    /// </summary>
    [Fact]
    public async Task SessionStore_Write_TargetTemporarilyLocked_RetriesAndSucceeds()
    {
        // Arrange: an existing session file that is locked briefly
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path, (_, _) => true, (_, _) => { });
        store.Write(CreateInfo("held", daemonPid: 1));
        var hold = new FileStream(store.GetPath("held"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(150, TestContext.Current.CancellationToken);
            await hold.DisposeAsync();
        }, TestContext.Current.CancellationToken);

        // Act: replace the file while it is locked
        store.Write(CreateInfo("held", daemonPid: 2));
        await release;

        // Assert: the new content is in place and no temporary file remains
        Assert.Equal(2, store.TryRead("held")!.DaemonPid);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    /// <summary>
    ///     Test that a replacement that fails leaves no temporary (token-bearing) file behind.
    /// </summary>
    [Fact]
    public void SessionStore_Write_ReplacementFails_RemovesTemporaryFile()
    {
        // Arrange: a directory where the session file should go, so the move cannot succeed
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path, (_, _) => true, (_, _) => { });
        Directory.CreateDirectory(Path.Combine(temp.Path, "blocked.json"));

        // Act: try to write the session
        var exception = Record.Exception(() => store.Write(CreateInfo("blocked")));

        // Assert: the I/O failure propagates and the temporary file is gone
        Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    /// <summary>
    ///     Test that listing returns sorted session ids and ignores other files.
    /// </summary>
    [Fact]
    public void SessionStore_ListIds_MixedFiles_ReturnsSortedSessionIds()
    {
        // Arrange: two sessions and unrelated files
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);
        store.Write(CreateInfo("b"));
        store.Write(CreateInfo("a"));
        File.WriteAllText(Path.Combine(temp.Path, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(temp.Path, "bad name.json"), "x");

        // Act: list
        var ids = store.ListIds();

        // Assert: only valid session files, sorted
        Assert.Equal(["a", "b"], ids);
    }

    /// <summary>
    ///     Test that listing a folder that does not exist yields an empty list.
    /// </summary>
    [Fact]
    public void SessionStore_ListIds_NoDirectory_ReturnsEmpty()
    {
        // Arrange: a store pointing at a folder that was never created
        using var temp = new TempDirectory();
        var store = new SessionStore(Path.Combine(temp.Path, "missing"));

        // Act: list the missing folder
        var ids = store.ListIds();

        // Assert: empty
        Assert.Empty(ids);
    }

    /// <summary>
    ///     Test that deleting removes the file and tolerates a second delete.
    /// </summary>
    [Fact]
    public void SessionStore_Delete_ExistingSession_RemovesFile()
    {
        // Arrange: a stored session
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);
        store.Write(CreateInfo("gone"));

        // Act: delete twice
        store.Delete("gone");
        store.Delete("gone");

        // Assert: the file is gone and nothing threw
        Assert.False(File.Exists(store.GetPath("gone")));
    }

    /// <summary>
    ///     Test that several parties deleting the same session file at the same moment all complete without error.
    /// </summary>
    [Fact]
    public async Task SessionStore_Delete_ConcurrentCallers_NoneThrows()
    {
        // Arrange: many rounds, each with a stored session and several parties that will delete it together
        const int rounds = 25;
        const int callers = 6;
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);

        for (var round = 0; round < rounds; round++)
        {
            store.Write(CreateInfo("busy"));

            // Act: delete from several threads at once
            var exceptions = await Task.WhenAll(Enumerable.Range(0, callers)
                .Select(_ => Task.Run(() => Record.Exception(() => store.Delete("busy")), Xunit.TestContext.Current.CancellationToken)));

            // Assert: nobody failed and the file is gone
            Assert.All(exceptions, Assert.Null);
            Assert.False(File.Exists(store.GetPath("busy")));
        }
    }

    /// <summary>
    ///     Test that the real liveness check accepts this process and rejects a recycled process id.
    /// </summary>
    [Fact]
    public void SessionStore_IsProcessAlive_CurrentProcess_TrueOnlyWithMatchingStartTime()
    {
        // Arrange: this process's id and start time
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        var start = self.StartTime.ToUniversalTime();

        // Act: check the real process with the right and a wrong start time
        var matching = SessionStore.IsProcessAlive(self.Id, start);
        var recycled = SessionStore.IsProcessAlive(self.Id, start.AddHours(-1));

        // Assert: matches with the right start time, not with a different one
        Assert.True(matching);
        Assert.False(recycled);
    }

    /// <summary>
    ///     Test that terminating a session kills the browser, the server, and the daemon by their recorded identities.
    /// </summary>
    [Fact]
    public void SessionStore_Terminate_Session_KillsBrowserServerAndDaemonWithTheirStartTimes()
    {
        // Arrange: a session record and a recording kill action
        using var temp = new TempDirectory();
        var killed = new List<(int Pid, DateTime Start)>();
        var store = new SessionStore(temp.Path, (_, _) => true, (pid, start) => killed.Add((pid, start)));
        var info = CreateInfo("busy", daemonPid: 111, serverPid: 222, browserPid: 333);

        // Act: terminate the session
        store.Terminate(info);

        // Assert: all three processes were killed together with the start times that identify them
        Assert.Equal(
            [(333, info.BrowserStartTimeUtc), (222, info.ServerStartTimeUtc), (111, info.DaemonStartTimeUtc)],
            killed);
    }

    /// <summary>
    ///     Test that processes that were never recorded (pid 0) are not terminated.
    /// </summary>
    [Fact]
    public void SessionStore_Terminate_UnrecordedBrowserAndServer_OnlyKillsDaemon()
    {
        // Arrange: a session that never got as far as a server or a browser
        using var temp = new TempDirectory();
        var killed = new List<int>();
        var store = new SessionStore(temp.Path, (_, _) => true, (pid, _) => killed.Add(pid));
        var info = CreateInfo("early", daemonPid: 111, serverPid: 0, browserPid: 0);

        // Act: terminate the session
        store.Terminate(info);

        // Assert: only the daemon was killed
        Assert.Equal([111], killed);
    }

    /// <summary>
    ///     Test that taking the start lock succeeds once and then fails for every later caller until it is released.
    /// </summary>
    [Fact]
    public void SessionStore_TryReserve_SecondCaller_IsRefusedUntilReleased()
    {
        // Arrange: a store whose lock owner (this process) counts as alive
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path, (_, _) => true, (_, _) => { });

        // Act: reserve the same id twice, release it, and reserve it again
        var first = store.TryReserve("demo");
        var second = store.TryReserve("demo");
        store.Release("demo");
        var afterRelease = store.TryReserve("demo");

        // Assert: only the first wins until the lock is released
        Assert.True(first);
        Assert.False(second);
        Assert.True(afterRelease);
    }

    /// <summary>
    ///     Test that a lock whose owner is gone is taken over, while a lock whose owner is alive is not.
    /// </summary>
    /// <param name="ownerAlive">Whether the lock's owner counts as alive.</param>
    /// <param name="expectedTakeover">Whether the second caller may take the lock.</param>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SessionStore_TryReserve_LockOfOtherOwner_IsTakenOverOnlyWhenOwnerIsGone(bool ownerAlive, bool expectedTakeover)
    {
        // Arrange: a lock held by a first caller, in a store that decides liveness as the test says
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path, (_, _) => ownerAlive, (_, _) => { });
        Assert.True(store.TryReserve("demo"));

        // Act: a second caller tries to take the lock
        var second = store.TryReserve("demo");

        // Assert: it succeeds only when the first owner is gone
        Assert.Equal(expectedTakeover, second);
    }

    /// <summary>
    ///     Test that a lock whose content cannot be read yet is respected while it is fresh and taken over once it is old.
    /// </summary>
    /// <param name="ageMinutes">How old the unreadable lock file is, in minutes.</param>
    /// <param name="expectedTakeover">Whether the caller may take the lock.</param>
    [Theory]
    [InlineData(0, false)]
    [InlineData(5, true)]
    public void SessionStore_TryReserve_UnreadableLock_IsTakenOverOnlyWhenOld(int ageMinutes, bool expectedTakeover)
    {
        // Arrange: an empty lock file, as seen while its owner is still writing it
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path, (_, _) => true, (_, _) => { });
        var lockPath = Path.Combine(temp.Path, "demo.lock");
        File.WriteAllText(lockPath, string.Empty);
        File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow.AddMinutes(-ageMinutes));

        // Act: try to take the lock
        var reserved = store.TryReserve("demo");

        // Assert: a fresh lock is held, an old one is stale
        Assert.Equal(expectedTakeover, reserved);
    }

    /// <summary>
    ///     Test that the start lock does not show up as a session.
    /// </summary>
    [Fact]
    public void SessionStore_TryReserve_Reserved_IsNotListedAsSession()
    {
        // Arrange: a store with a lock held
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path, (_, _) => true, (_, _) => { });
        store.TryReserve("demo");

        // Act: list the sessions and look the id up
        var ids = store.ListIds();
        var lookup = store.Lookup("demo", out _);

        // Assert: a lock is not a session
        Assert.Empty(ids);
        Assert.Equal(SessionLookup.Unknown, lookup);
    }

    /// <summary>
    ///     Test that when many callers take the start lock for the same id at once exactly one wins.
    /// </summary>
    [Fact]
    public void SessionStore_TryReserve_ConcurrentCallers_ExactlyOneWins()
    {
        // Arrange: an empty store and a number of simultaneous claimants
        const int claimants = 16;
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path, (_, _) => true, (_, _) => { });

        // Act: all claimants reserve the same id in parallel
        var results = Enumerable.Range(0, claimants)
            .AsParallel()
            .WithDegreeOfParallelism(claimants)
            .Select(_ => store.TryReserve("race"))
            .ToList();

        // Assert: exactly one claimant won
        Assert.Equal(1, results.Count(won => won));
    }

    /// <summary>
    ///     Test that on Unix the session directory, session file, and lock file are created owner-only
    ///     (0700 for the directory, 0600 for files).
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void SessionStore_Write_UnixPlatform_CreatesOwnerOnlyDirectoryAndFile()
    {
        // Arrange: a store in a directory that does not exist yet (Windows has no Unix modes to check)
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes do not exist on Windows; the per-user temporary directory provides the privacy there.");
        using var temp = new TempDirectory();
        var directory = Path.Combine(temp.Path, "sessions");
        var store = new SessionStore(directory);

        // Act: write a session and take a lock, which create the directory and files
        store.Write(CreateInfo("demo"));
        var reserved = store.TryReserve("locked");

        // Assert: directory 0700, session file 0600, lock file 0600, and no stray temporary files
        Assert.True(reserved);
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(directory));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(store.GetPath("demo")));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(directory, "locked.lock")));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        store.Release("locked");
    }
}
