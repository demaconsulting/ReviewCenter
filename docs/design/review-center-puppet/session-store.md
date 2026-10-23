## SessionStore

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`SessionStore` persists the state of each Puppet session as a file and decides whether a session is usable. The
session file is the only shared state between the short-lived client commands and the long-lived daemon. The store
verifies that the daemon of a session is really running and removes the file, and any server the dead daemon left
behind, when it is not. It holds no state besides its directory and is safe to use from several processes.

### Data Model

**Directory**: `string` - the folder that holds the session files, by default `reviewcenter-puppet` in the system
temporary folder.

**SessionInfo**: a record with the fields `SessionId`, `Status`, `ControlPort`, `ControlToken`, `ServerUrl`,
`DaemonPid`, `DaemonStartTimeUtc`, `ServerPid`, `ServerStartTimeUtc`, `BrowserPid`, `BrowserStartTimeUtc`, and
`Error`. It is stored as indented JSON with enumeration values written as text, in `<session id>.json`.

**ProcessIdentity**: a process id together with its start time. Both must match for a process to count as the
recorded one.

**Start lock**: a file `<session id>.lock` next to the session file that reserves the id while a `start` command is
launching its daemon. It records the identity of the process that owns it. It is never listed as a session.

**Permissions**: the session folder and every session and lock file are readable by their owner only, because the
session file holds the control token. On Unix platforms the folder is created with mode 0700 and the files with
mode 0600, requested at creation time so there is no moment at which they are more widely accessible. On Windows
the system temporary folder is already private to the current user, so no additional permissions are set.

**SessionStatus**: `Starting` (the daemon is still bringing up the server and the browser), `Ready` (commands can be
served), and `Failed` (start-up failed and `Error` holds the reason).

**SessionLookup**: `Unknown` (no session file exists), `Live` (the daemon is running), and `Stale` (the file existed
but its daemon is gone, and it has been cleaned up).

### Key Methods

**GetPath**: Returns the file path of a session. Throws `ArgumentException` unless the id is valid according to
`CommandLine.IsValidSessionId`, so an id can never point outside the session folder.

**Write**: Creates the folder if needed and writes the session to a temporary file beside the target, then moves it
into place, so a reader sees either the old or the new content and never a partial file. The folder and the file,
including the temporary file, are created owner-only on Unix platforms (see Permissions). A move that is refused for
a moment (a sharing or access error while a reader has the target open) is retried about twenty times, 25
milliseconds apart; if it still fails, the temporary file, which holds the control token, is deleted and the error
propagates to the caller.

**TryReserve**: Reserves a session id for a `start` command by creating the lock file with an exclusive create
(`FileMode.CreateNew`), so of several simultaneous callers exactly one succeeds; the others get `false`. When the
lock already exists, it is taken over only if it is stale: its recorded owner process is no longer alive, or it
cannot be read and is older than a grace period of 10 seconds (a lock that was just created and is not yet written
counts as held). The takeover renames the stale lock aside before deleting it and then tries the exclusive create
once more. The takeover of a stale lock is best effort: two callers that reclaim the same stale lock at the same
moment together with a third fresh caller could in theory all succeed, which is accepted because the owner of a
stale lock is already dead and the daemon launch itself is still guarded by the session lookup that follows. On
Windows, a lock file that is still pending deletion (another handle is open on it) makes the exclusive create fail
with an access-denied error; this is treated as the lock being held, so the caller is refused instead of failing.

**Release**: Deletes the lock file of a session id, if it exists. Failure to delete is ignored.

**TryRead**: Reads a session without checking the daemon. Returns null when the file is missing, is not valid JSON,
or stays locked after the retries described under Reading. It never deletes anything, so a null result is safe for
callers that only want to display a session.

**Reading**: Both `TryRead` and `Lookup` open the file with full sharing (read, write, and delete), so the daemon's
replacement can proceed while a read is in flight. A sharing or access error is retried about twenty times, 25
milliseconds apart (roughly half a second). The result is classified as read, missing, corrupt, or unavailable. Only
missing and corrupt files justify cleanup; a file that stays unavailable says nothing about the daemon.

**Delete**: Removes a session file if it exists; deleting a missing file is not an error. The daemon's own cleanup
and a `start` that aborts can delete the same file at the same moment, and on Windows a delete that loses that race
can fail with a sharing or access error, so a failed delete is retried a few times after a short delay and ends as
soon as the file is gone.

**ListIds**: Returns the ids of the `.json` files in the folder whose names are valid ids (lock files are not
sessions), in ordinal sorted order. It returns an empty list when the folder does not exist.

**Lookup**: Returns `Unknown` when there is no file. Returns `Live` with the session when the recorded daemon
process is alive. Otherwise it stops the server and the browser process that the file records, if any (by their
recorded identities, so a newer session's processes are never touched), then removes the file with `DeleteIfSame`
and returns `Stale`. A corrupt file is therefore also stale. If `DeleteIfSame` reports that the file now holds a
different session (a concurrent `start` replaced the stale one in between), nothing is deleted and the lookup
examines the new file instead; this repeats at most three times before a `PuppetException` says the session kept
changing. If a `start` holds the reservation for the id, the stale file is left for that `start` to replace or
clean and the result is still `Stale`; the `start` command itself passes `startLockHeld` because it already owns
the reservation. A file that stays locked or inaccessible after the
retries is neither: `Lookup` throws a `PuppetException` saying the session file is temporarily unreadable, and
deletes nothing and stops no process, so a live session can never be removed because of a transient I/O error. A file
that vanishes between the existence check and the read is reported as `Unknown`.

**DeleteIfSame**: Deletes the file of a session on behalf of a caller that examined it earlier (for example a `stop`
or a stale lookup), but only if it still holds that session. It takes the start reservation first, so no `start` can
publish a replacement between the check and the delete; when another start holds the reservation it returns `Busy`
and deletes nothing. It then re-reads the file: a missing file counts as `Deleted`, an unreadable one is `Busy`, a
different session (compared with `IsSameSession`) is `Replaced` and is kept, and a matching one is deleted. A
corrupt file matches only an examination that found it corrupt (`expected` is `null`). The reservation is released
afterwards unless the caller passed `startLockHeld`.

**IsSameSession**: True when two records have the same session id, daemon process id, daemon start time, and control
token, which identifies one daemon instance of a session even when other fields (status, recorded server and
browser) have changed.

**IsDaemonAlive**: Checks the recorded daemon of a session.

**IsAlive**: Checks a process identity.

**HasLiveProcesses**: True when the recorded daemon, or the recorded server or browser (when their ids are set), is
still alive by id and start time. Callers use it to confirm a cleanup before they delete a session file.

**Terminate**: Ends one process identity, or the browser, the server, and the daemon of a session, each together with
its child processes. Every process is ended only when its id and start time match, so a recycled process id is
never ended and nothing is ended by name.

**IsProcessAlive**: Returns true only when a process with the given id exists, has not exited, and started within two
seconds of the recorded start time. The start time guards against the operating system reusing a process id.

**KillProcess**: Ends a process and its children, but only when it is the recorded process. The process is acquired
once and its operating system handle is opened before the start time is compared, and the same process object is
then killed. Where a handle pins one process (Windows), the process that was checked is the process that is ended,
so a process id recycled between the check and the kill cannot be hit. On platforms without such a handle the check
and the kill are adjacent operations on one object, which leaves only a very small window; the operating system
offers no portable way to close it entirely.

### Error Handling

File problems are absorbed where an answer is still possible: `TryRead` turns a missing, corrupt, or persistently
locked file into null, `Lookup` reports a persistently locked file as an error without cleaning up, and the process
checks turn "no such process", "already exited", and "access denied" into "not alive". Invalid ids
throw `ArgumentException`. Other file system errors, such as a folder that cannot be written, propagate to the
caller.

### Dependencies

- **CommandLine**: provides `IsValidSessionId`.

### Callers

- **PuppetProgram**: reserves and releases session ids, looks sessions up, lists them, deletes failed sessions, and
  terminates sessions and daemons that do not stop or never finished starting.
- **DaemonHost**: writes the `Starting`, `Ready`, and `Failed` states and deletes the file when the session ends.
