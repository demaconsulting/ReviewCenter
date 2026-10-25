### GitRunner

#### Verification Approach

`GitRunner` is verified with tests defined in `GitRunnerTests.cs`. The start of git is verified without running git, by
inspecting the process start information that `BuildStartInfo` returns: the argument list, the environment, and the
absence of the token anywhere but its one variable. Redaction and failure descriptions are verified as pure functions
with known secrets in every form git might print them. The behavior of the process (output, exit code, the output limit,
the time limit, and the version check) is verified against a real git, against an executable name that does not exist,
and against a script that reports an old version, using the test-only constructor that takes the executable. The
time limit is verified with a local server on the loopback address that accepts a connection and never answers, so that
the runner must stop git itself; the credential header and the redaction of a refusal are verified with a local server
that answers `401` and records the request. Tests that need git skip themselves when git is not installed.

#### Test Environment

N/A - standard test environment (a temporary cache folder is created by each test, and the tests that need it use a
local server on the loopback address and the installed git).

#### Acceptance Criteria

- All tests pass with zero failures (or skip because git is not installed).
- Git is started with an argument list and no shell; credentials are in no argument; the token is in the child
  environment only, as the value of a numbered configuration variable, and only when both a user name and a token exist.
- The child environment is curated: it cannot read the system or user configuration, cannot prompt, and runs no askpass
  or credential helper.
- Every form of a secret (the token, the user name and token, the base64 header value, the escaped token, and the user
  information of an address) is replaced in text, and missing text or secrets are handled.
- A failed call is described with the repository name and without any secret or location (a URL, a local path, and the
  host name are replaced by the name), and an authentication failure gets a clear
  message.
- A missing or too old git is `GitUnavailable`, as is a cache folder that cannot be created or whose path is malformed;
  output over the limit is `GitAccessFailed`; a call that never finishes is stopped at the time limit as `GitTimeout`.
- Git sends the token as a basic authorization header, and a refusal is reported as denied access without the token or
  its encodings.

#### Test Scenarios

**GitRunner_BuildStartInfo_TokenOnlyInEnvironment**: The start information for a call with a user name and token uses the
argument list and no shell; no argument holds the token or the user name; the token reaches the child only through the
numbered configuration variables as an authorization header for the location. This scenario is tested by
`GitRunner_BuildStartInfo_TokenOnlyInEnvironment`.

**GitRunner_BuildStartInfo_Anonymous_HasCuratedEnvironment**: For a call without credentials no configuration variable is
set; the environment cannot read the system or user configuration, cannot prompt, and runs no askpass program. This
scenario is tested by `GitRunner_BuildStartInfo_Anonymous_HasCuratedEnvironment`.

**GitRunner_Redact_RemovesEverySecretForm**: The token, the user name and token, the base64 header value, and the user
information of an address are removed from text. This scenario is tested by `GitRunner_Redact_RemovesEverySecretForm`.

**GitRunner_Redact_EdgeCases**: A URL-escaped token is removed too; null text, a missing token, and a missing user name
are handled. This scenario is tested by `GitRunner_Redact_EdgeCases`.

**GitRunner_Redact_ReplacesLocationWithName**: The registered location is replaced by the repository name in every form
git prints it: as registered, with a trailing slash or `.git` added or removed, with forward or back slashes for a
local path, inside quotes, with user information (also masked), a `file://` URL, and the host name in a connection
error; nothing of the location or of its user information remains. This scenario is tested by
`GitRunner_Redact_ReplacesLocationWithName`.

**GitRunner_Redact_NoLocation_LeavesTextAlone**: Without a location nothing is replaced, and text that does not contain
the location is unchanged. This scenario is tested by `GitRunner_Redact_NoLocation_LeavesTextAlone`.

**GitRunner_FailureFor_DescribesFailuresClearly**: A failure is described with the repository name and the redacted first
line of the error, and an authentication failure says access was denied and what an administrator must check. This
scenario is tested by `GitRunner_FailureFor_DescribesFailuresClearly`.

**GitRunner_FailureFor_NeverContainsLocation**: For a URL and for a local path, and for a denied access, an error text
that still holds the location gives a message that names the repository and has no part of its location. This scenario
is tested by `GitRunner_FailureFor_NeverContainsLocation`.

**GitRunner_RunCheckedAsync_UnknownLocalRepository_MessageHasNameNotPath**: A real git call on a local path that does
not exist gives a message with the repository name and no part of the path. This scenario is tested by
`GitRunner_RunCheckedAsync_UnknownLocalRepository_MessageHasNameNotPath`.

**GitRunner_RunCheckedAsync_UnreachableUrl_MessageHasNameNotAddress**: A real git call to an address where nothing
listens gives a message with the repository name and neither the address nor the host name. This scenario is tested by
`GitRunner_RunCheckedAsync_UnreachableUrl_MessageHasNameNotAddress`.

**GitRunner_RunAsync_MissingGit_ThrowsGitUnavailable**: An executable that does not exist gives `GitUnavailable`. This
scenario is tested by `GitRunner_RunAsync_MissingGit_ThrowsGitUnavailable`.

**GitRunner_RunAsync_OldGit_ThrowsGitUnavailable**: A git that reports a version older than the minimum is refused as
unavailable. This scenario is tested by `GitRunner_RunAsync_OldGit_ThrowsGitUnavailable`.

**GitRunner_RunAsync_HomeFolderNotWritable_ThrowsGitUnavailable**: When the cache path cannot be created (it lies below
a regular file), a call fails with `GitUnavailable`; the message names `Git:CachePath` but not the path, and no inner
exception is chained. This scenario is tested by `GitRunner_RunAsync_HomeFolderNotWritable_ThrowsGitUnavailable`.

**GitRunner_RunAsync_MalformedCachePath_ThrowsGitUnavailable**: A cache path with a null character, or with a name that
is too long for any file system, makes a call fail with `GitUnavailable` instead of an unhandled exception; the message
names `Git:CachePath` but not the path, and no inner exception is chained. This scenario is tested by
`GitRunner_RunAsync_MalformedCachePath_ThrowsGitUnavailable`.

**GitRunner_RunAsync_RealGit_ReturnsOutputAndExitCode**: A real git call returns its output, and a failing call
returns its
exit code and error text. This scenario is tested by `GitRunner_RunAsync_RealGit_ReturnsOutputAndExitCode`.

**GitRunner_RunAsync_OutputOverCap_ThrowsGitAccessFailed**: Output beyond the limit stops the call with
`GitAccessFailed`. This scenario is tested by `GitRunner_RunAsync_OutputOverCap_ThrowsGitAccessFailed`.

**GitRunner_RunAsync_ServerThatNeverAnswers_ThrowsGitTimeoutAndStopsGit**: A call to a server that never answers is
stopped at the time limit with `GitTimeout`, and git is no longer running. This scenario is tested by
`GitRunner_RunAsync_ServerThatNeverAnswers_ThrowsGitTimeoutAndStopsGit`.

**GitRunner_RunCheckedAsync_RefusedCredentials_SendsHeaderAndHidesSecret**: Git sends the token as a basic authorization
header to a local server that refuses it; the refusal is `GitAccessFailed` with the denied-access message, and neither
the token nor its encodings are in the message. This scenario is tested by
`GitRunner_RunCheckedAsync_RefusedCredentials_SendsHeaderAndHidesSecret`.
