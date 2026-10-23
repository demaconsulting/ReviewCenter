## SessionStore

### Verification Approach

`SessionStore` is verified with unit tests defined in `SessionStoreTests.cs`. The store is created over a temporary
folder, and the process liveness check and the kill action are injected as delegates, so stale and live sessions can
be simulated without real processes and a recording delegate shows which process would have been stopped. The one
exception is the static `IsProcessAlive` check, which is verified against the test process itself.

### Test Environment

N/A - standard test environment. Each test creates its own temporary folder with the `TempDirectory` helper.

### Acceptance Criteria

- All unit tests pass with zero failures.
- A written session reads back as an equal record, and a missing or corrupt file reads as null.
- A session file that is locked or being replaced for a moment is read again and found live; one that stays locked
  is reported as an error, and its file is kept and no process is stopped.
- A session file replaced over and over while it is looked up is never reported as stale.
- A replacement refused for a moment is retried and succeeds; a replacement that fails leaves no temporary file.
- An id that could leave the session folder is rejected.
- A session without a file is unknown, and a session whose daemon runs is live and keeps its file.
- A session whose daemon is gone, or whose file is corrupt, is stale, its file is deleted, and the orphaned browser
  and server are stopped.
- A stale session's file is deleted only when it still holds the examined session and no start holds the
  reservation; a session that a concurrent start published in the meantime is kept and examined itself, and a
  session that keeps changing is reported as an error. A delete on behalf of a caller that examined a session
  (`DeleteIfSame`) keeps a different session, leaves the file while another start holds the reservation, and treats a
  missing file as deleted.
- The browser, the server, and the daemon of a session are ended with the process ids and start times that were
  recorded; a browser or server that was never recorded is not touched. A real process is ended only when its start
  time matches, and the check can tell whether any recorded process is still alive.
- Exactly one of several simultaneous callers reserves a session id; the rest are refused until it is released; a
  reservation is taken over only when its owner is gone or its content is unreadable and older than the grace period.
- On Unix platforms the session folder is created with mode 0700 and a session file with mode 0600.
- Listing returns sorted ids, ignores other files (including reservations), and returns an empty list when the folder
  does not exist.
- Deleting twice, or from several threads at once, does not fail.
- The current process is alive only when the recorded start time matches.

### Test Scenarios

**SessionStore_WriteRead_RoundTrip_ReturnsSameRecord**: A record is written and read back; the record is equal and
`demo.json` exists in the folder. This scenario is tested by `SessionStore_WriteRead_RoundTrip_ReturnsSameRecord`.

**SessionStore_TryRead_MissingSession_ReturnsNull**: Reading a session that was never written returns null. This
scenario is tested by `SessionStore_TryRead_MissingSession_ReturnsNull`.

**SessionStore_TryRead_CorruptFile_ReturnsNull**: A session file that contains garbage reads as null. This scenario
is tested by `SessionStore_TryRead_CorruptFile_ReturnsNull`.

**SessionStore_GetPath_InvalidId_ThrowsArgumentException**: The id `..\x` causes an `ArgumentException`. This
scenario is tested by `SessionStore_GetPath_InvalidId_ThrowsArgumentException`.

**SessionStore_Lookup_NoFile_ReturnsUnknown**: Looking up a session without a file returns `Unknown` and no
record. This scenario is tested by `SessionStore_Lookup_NoFile_ReturnsUnknown`.

**SessionStore_Lookup_LiveDaemon_ReturnsLive**: With a liveness check that reports the daemon running, the lookup
returns `Live` with the stored server URL, and the file remains. This scenario is tested by
`SessionStore_Lookup_LiveDaemon_ReturnsLive`.

**SessionStore_ReadStale_DeadDaemon_CleansUpAndReports**: With a liveness check that reports the daemon dead, the
lookup returns `Stale` without a record, the file is deleted, and the recorded browser and server process ids were
passed to the kill action, in that order, while the dead daemon was not. This scenario is tested by
`SessionStore_ReadStale_DeadDaemon_CleansUpAndReports`.

**SessionStore_Lookup_CorruptFile_CleansUpAndReportsStale**: A corrupt session file is reported as `Stale` and
deleted. This scenario is tested by `SessionStore_Lookup_CorruptFile_CleansUpAndReportsStale`.

**SessionStore_Lookup_SessionReplacedDuringCleanup_KeepsReplacementAndReportsItLive**: The liveness check of a stale
session's dead daemon publishes a replacement session (new daemon, new token) from another store, as a concurrent
`start` would. The lookup does not delete the replacement: it examines it and returns `Live` with the replacement
record, the file still holds the replacement, and only the old session's browser and server were stopped. This
scenario is tested by `SessionStore_Lookup_SessionReplacedDuringCleanup_KeepsReplacementAndReportsItLive`.

**SessionStore_Lookup_StartLockHeldByAnotherStart_LeavesFileAndReportsStale**: A stale session's id is reserved by a
start whose owner is alive. The lookup returns `Stale` but the file is not deleted. This scenario is tested by
`SessionStore_Lookup_StartLockHeldByAnotherStart_LeavesFileAndReportsStale`.

**SessionStore_Lookup_SessionKeepsBeingReplaced_ThrowsAndDeletesNothing**: Every liveness check publishes yet another
dead session. After three attempts the lookup throws a `PuppetException` and the latest file is still there. This
scenario is tested by `SessionStore_Lookup_SessionKeepsBeingReplaced_ThrowsAndDeletesNothing`.

**SessionStore_DeleteIfSame_SameSession_DeletesFileAndReleasesLock**: A stored session is deleted as the examined
session; the outcome is `Deleted`, the file is gone, and the reservation can be taken again afterwards. This scenario
is tested by `SessionStore_DeleteIfSame_SameSession_DeletesFileAndReleasesLock`.

**SessionStore_DeleteIfSame_ReplacedSession_KeepsFile**: The file holds a session with another daemon, and then one
with another token. Each delete of the examined session returns `Replaced` and the file is kept. This scenario is
tested by `SessionStore_DeleteIfSame_ReplacedSession_KeepsFile`.

**SessionStore_DeleteIfSame_StartLock_SkipsForOthersAndAllowsOwner**: While a start holds the reservation, a delete by
a bystander returns `Busy` and keeps the file; the same delete with `startLockHeld` returns `Deleted` and leaves the
reservation held. This scenario is tested by `SessionStore_DeleteIfSame_StartLock_SkipsForOthersAndAllowsOwner`.

**SessionStore_DeleteIfSame_MissingAndCorruptFiles_FollowTheExaminedState**: A missing file counts as `Deleted`. A
corrupt file is kept (`Replaced`) when the caller examined a readable session and is deleted when the caller
examined it as corrupt. This scenario is tested by
`SessionStore_DeleteIfSame_MissingAndCorruptFiles_FollowTheExaminedState`.

**SessionStore_IsSameSession_ComparesDaemonIdentityAndToken**: Records that differ only in status or server details
are the same session; records that differ in daemon process id, daemon start time, or control token are not. This
scenario is tested by `SessionStore_IsSameSession_ComparesDaemonIdentityAndToken`.

**SessionStore_Lookup_FileTemporarilyLocked_RetriesAndReportsLive**: The file of a live session is held exclusively
for about 150 milliseconds while it is looked up; the lookup retries, returns `Live`, nothing is deleted, and no
process is stopped. This scenario is tested by `SessionStore_Lookup_FileTemporarilyLocked_RetriesAndReportsLive`.

**SessionStore_Lookup_FileStaysLocked_ThrowsAndKeepsSession**: The file stays locked for the whole lookup, with a
liveness check that would report the daemon dead; the lookup throws a `PuppetException` saying the file is
temporarily unreadable, the file remains and can be read afterwards, and no process is stopped. This scenario is
tested by `SessionStore_Lookup_FileStaysLocked_ThrowsAndKeepsSession`.

**SessionStore_WriteAndLookup_ConcurrentReplacement_NeverReportsStale**: One thread rewrites a live session 300
times while another looks it up continuously; every lookup returns `Live`, nothing is stopped, the file remains, and
no temporary file is left. This scenario is tested by
`SessionStore_WriteAndLookup_ConcurrentReplacement_NeverReportsStale`.

**SessionStore_Write_TargetTemporarilyLocked_RetriesAndSucceeds**: The existing file is held exclusively for about
150 milliseconds while a new record is written over it; the write retries, the new content is in place, and no
temporary file remains. This scenario is tested by `SessionStore_Write_TargetTemporarilyLocked_RetriesAndSucceeds`.

**SessionStore_Write_ReplacementFails_RemovesTemporaryFile**: A folder occupies the place of the session file so the
move can never succeed; the write fails with an I/O error and no temporary file remains. This scenario is tested by
`SessionStore_Write_ReplacementFails_RemovesTemporaryFile`.

**SessionStore_ListIds_MixedFiles_ReturnsSortedSessionIds**: With two sessions and unrelated files in the folder,
the listing returns exactly the two ids in sorted order. This scenario is tested by
`SessionStore_ListIds_MixedFiles_ReturnsSortedSessionIds`.

**SessionStore_ListIds_NoDirectory_ReturnsEmpty**: A store pointing at a folder that was never created returns an
empty list. This scenario is tested by `SessionStore_ListIds_NoDirectory_ReturnsEmpty`.

**SessionStore_Delete_ExistingSession_RemovesFile**: A stored session is deleted twice; the file is gone and the
second deletion does not fail. This scenario is tested by `SessionStore_Delete_ExistingSession_RemovesFile`.

**SessionStore_Delete_ConcurrentCallers_NoneThrows**: In 25 rounds, a stored session is deleted by six threads at the
same moment; no thread fails and the file is gone after each round. This scenario is tested by
`SessionStore_Delete_ConcurrentCallers_NoneThrows`.

**SessionStore_Terminate_Session_KillsBrowserServerAndDaemonWithTheirStartTimes**: A session with a recorded
browser, server, and daemon is terminated; the kill action receives all three process ids, each with its own
recorded start time. This scenario is tested by
`SessionStore_Terminate_Session_KillsBrowserServerAndDaemonWithTheirStartTimes`.

**SessionStore_Terminate_UnrecordedBrowserAndServer_OnlyKillsDaemon**: A session that recorded no browser and no
server is terminated; only the daemon is passed to the kill action. This scenario is tested by
`SessionStore_Terminate_UnrecordedBrowserAndServer_OnlyKillsDaemon`.

**SessionStore_KillProcess_RealProcess_KillsOnlyWhenStartTimeMatches**: A real idle child process is started. Killing
it with a start time that belongs to another process leaves it running; killing it with its real start time ends it.
This scenario is tested by `SessionStore_KillProcess_RealProcess_KillsOnlyWhenStartTimeMatches`. The identity check
and the kill act on one acquired process object; the remaining platform-dependent window between them cannot be
provoked deterministically, so it is covered by the design rather than by a test.

**SessionStore_KillProcess_NoSuchProcess_DoesNothing**: Killing a process id that no process uses throws nothing. This
scenario is tested by `SessionStore_KillProcess_NoSuchProcess_DoesNothing`.

**SessionStore_HasLiveProcesses_ChecksDaemonServerAndBrowserOnly_WhenRecorded**: With only the daemon, only the server,
or only the browser of a record alive, the check is true; with only an unrelated process alive it is false; and a
record with no server and no browser never consults process id 0. This scenario is tested by
`SessionStore_HasLiveProcesses_ChecksDaemonServerAndBrowserOnly_WhenRecorded`.

**SessionStore_TryReserve_SecondCaller_IsRefusedUntilReleased**: The first reservation of an id succeeds, a second
is refused, and after the release a new reservation succeeds. This scenario is tested by
`SessionStore_TryReserve_SecondCaller_IsRefusedUntilReleased`.

**SessionStore_TryReserve_ConcurrentCallers_ExactlyOneWins**: Many threads reserve the same id at once; exactly one
succeeds. This scenario is tested by `SessionStore_TryReserve_ConcurrentCallers_ExactlyOneWins`.

**SessionStore_TryReserve_LockOfOtherOwner_IsTakenOverOnlyWhenOwnerIsGone**: A reservation recorded for another
process is refused while that process is alive and taken over when it is gone. This scenario is tested by
`SessionStore_TryReserve_LockOfOtherOwner_IsTakenOverOnlyWhenOwnerIsGone`.

**SessionStore_TryReserve_UnreadableLock_IsTakenOverOnlyWhenOld**: A reservation whose content cannot be read is
treated as held while it is young and taken over once it is older than the grace period. This scenario is tested by
`SessionStore_TryReserve_UnreadableLock_IsTakenOverOnlyWhenOld`.

**SessionStore_TryReserve_Reserved_IsNotListedAsSession**: A reserved id does not appear in the session listing.
This scenario is tested by `SessionStore_TryReserve_Reserved_IsNotListedAsSession`.

**SessionStore_Write_UnixPlatform_CreatesOwnerOnlyDirectoryAndFile**: On Unix platforms, a session is written into
a folder that did not exist; the folder has mode 0700 and the file mode 0600. The test is skipped with a stated
reason on Windows, where the per-user temporary folder provides the isolation and no mode is set, so the requirement
uses Linux and macOS source filters. This scenario is tested by
`SessionStore_Write_UnixPlatform_CreatesOwnerOnlyDirectoryAndFile`.

**SessionStore_IsProcessAlive_CurrentProcess_TrueOnlyWithMatchingStartTime**: The check is true for the test
process with its real start time and false with a start time one hour earlier. This scenario is tested by
`SessionStore_IsProcessAlive_CurrentProcess_TrueOnlyWithMatchingStartTime`.
