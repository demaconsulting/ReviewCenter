## ContentSources

### Verification Approach

The `ContentSources` subsystem is verified by subsystem tests defined in `ContentSourcesSubsystemTests.cs`. The tests
use the real `RepositoryService`, `RepositoryStore`, `ReviewCenterDbContext`, `CredentialProtector`, `GitRunner`, and
`GitSource` together, over a real SQLite database file and a real temporary git repository (created with the installed
git by the test helper `GitTestSupport`), because the subsystem exists to join these parts: a registration made by a
project administrator or system administrator, a token that is stored only encrypted, and the reading of a real
repository through the access data
built from the registration. The Data Protection key ring is a real folder. The tests skip themselves when git is not
installed. The units are verified in detail by their own unit tests: `GitRunner` (the start of git, redaction, limits,
and failures, against a real git, a missing git, and a local server that never answers or refuses credentials),
`GitSource` (clone, fetch, revisions, change lists, and blobs against real repositories), and `CredentialProtector` (the
round trip, the key ring, and the purpose).

### Test Environment

Tests create temporary SQLite database files, key ring folders, mirror folders, and git repositories in private
temporary folders, which are removed after the test run. The tests that run git need git on the test machine; they skip
themselves when it is not found, and the tests of the start information, redaction, and failure descriptions need no
git. No network access is needed: repositories are local, and one test starts a local server on the loopback address.

### Acceptance Criteria

- All subsystem tests pass with zero failures (or skip because git is not installed).
- A repository that an administrator of its project registered with a token is stored with the token only encrypted, and
  is read through
  the real git source: its revision resolves, the change between two commits is listed, and the exact bytes of a file are
  read.
- A disabled repository is not available for reading.
- The review-sets of a repository are read from the configuration file that git returns for a commit, and the history of
  its commits is answered.
- The token reaches git only through the environment of the child process, never through an argument, the mirror
  configuration, a message, or a stored value.
- A failed git call, a missing git, a call that never ends, and output over the limit are reported as clear domain rule
  errors without secrets.

### Test Scenarios

**ContentSourcesSubsystem_RegisteredRepository_IsReadThroughTheRealGitSource**: An administrator registers, in a
project, a private
https repository with a user name and a token and a local repository without credentials, through the service over a
real database with a persisted key ring. The stored token text is not the token, the access data built for the private
repository holds the decrypted token while its text form does not show it, and a session of the real git source on the
local repository resolves a branch to the newest commit, lists the change between the two commits, and reads the exact
bytes of the changed file. This scenario is tested by
`ContentSourcesSubsystem_RegisteredRepository_IsReadThroughTheRealGitSource`.

**ContentSourcesSubsystem_DisabledRepository_IsNotAvailableForReading**: A registered repository is disabled; it is not
offered for selection, and building the access data for it is refused with `RepositoryDisabled`, so it cannot be
opened. This scenario is tested by `ContentSourcesSubsystem_DisabledRepository_IsNotAvailableForReading`.

**ContentSourcesSubsystem_ReviewSets_AreReadFromTheRepositoryConfiguration**: A local repository holds a
`.reviewmark.yaml` with one review-set in its first commit and a change in its second. The real git source lists the
head commit, reads the bytes of the configuration file, and the real parser returns the set with its identifier, title,
and paths; the ancestor check says yes from the first commit to the second and no the other way. This scenario is tested
by `ContentSourcesSubsystem_ReviewSets_AreReadFromTheRepositoryConfiguration`.

**Units**: The start of git without a shell and with a curated environment, the redaction of secrets, the limits and the
failures are verified in the unit tests of `GitRunner`; the mirror, the revisions, the change lists, the blobs, the
ancestor check, and the serialization per repository in the unit tests of `GitSource`; the reading and the limits of
the configuration file in the unit tests of `ReviewMarkConfigParser`; and the encryption and the key ring in the unit
tests of `CredentialProtector`. The starting of the server with a persisted key ring, so that a token stays readable
after a
restart, is verified in the `Hosting` subsystem.
