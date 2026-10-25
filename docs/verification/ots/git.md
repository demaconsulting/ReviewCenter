## git Verification

This document provides the verification evidence for the git OTS software item. Requirements for this OTS item are
defined in the git OTS Software Requirements document.

### Required Functionality

git mirrors a repository, resolves revisions, lists the files of a commit and the changes between two commits, and
gives the exact bytes of a file, when it is run as a child process without a shell. It sends credentials that it is
given in its process environment as an HTTP header without storing them. Passing tests confirm that both function
correctly.

### Verification Approach

git has no self-validation mode and is verified by the project's own tests, which run the real program against real
repositories created in a private temporary folder and against a local server that refuses credentials; no substitute
is used, because the behavior of the real program is what is verified. Each scenario names a specific test method that
depends on the program under verification. A passing pipeline run for all scenarios constitutes evidence that both
requirements are satisfied.

### Test Scenarios

#### GitRunner_RunAsync_RealGit_ReturnsOutputAndExitCode

**Scenario**: The runner starts the installed git program with a call that succeeds and with a call that fails.

**Expected**: The program runs without a shell, the output of the first call is returned, and the failing call returns
its exit code and error text.

**Requirement coverage**: `ReviewCenter-OTS-Git-Access`.

#### GitSource_ResolveRevision_BranchTagAndId_ResolveAndBadOnesAreRefused

**Scenario**: A branch, a tag, and a full commit identity of a mirrored repository are resolved, and an unknown and an
unsafe revision are resolved too.

**Expected**: The first three give the commit identity, and the others are refused.

**Requirement coverage**: `ReviewCenter-OTS-Git-Access`.

#### GitSource_ListChanges_BetweenCommits_ListsChangesAndSkipsSpecialEntries

**Scenario**: The changes between two commits are listed, with modified, added, deleted, and renamed files, a symbolic
link, a submodule, and a change of the file mode only.

**Expected**: git reports each change with rename detection, so that the source can list a rename as a deletion and
an addition and skip the link and the submodule.

**Requirement coverage**: `ReviewCenter-OTS-Git-Access`.

#### GitSource_ListChanges_NoBaseline_ListsEveryFileAsAdded

**Scenario**: The files of a commit are listed without an earlier revision.

**Expected**: Every file is listed as added.

**Requirement coverage**: `ReviewCenter-OTS-Git-Access`.

#### GitSource_ReadBlob_ReadsExactBytesAndEnforcesTheLimit

**Scenario**: A file with binary content and carriage returns is read, and a file over the size limit is read.

**Expected**: The bytes are returned unchanged, and the oversized file is refused.

**Requirement coverage**: `ReviewCenter-OTS-Git-Access`.

#### GitRunner_RunCheckedAsync_RefusedCredentials_SendsHeaderAndHidesSecret

**Scenario**: git fetches from a local server that refuses the credentials, with a token given in the environment of
the process.

**Expected**: The server receives the credentials as an HTTP header, the failure is reported as an authentication
failure, and the token appears nowhere in the message.

**Requirement coverage**: `ReviewCenter-OTS-Git-Credentials`.

#### GitSource_Mirror_NeverStoresTheToken

**Scenario**: A repository with a token is mirrored, and the configuration of the mirror is read.

**Expected**: The configuration of the mirror holds no part of the token.

**Requirement coverage**: `ReviewCenter-OTS-Git-Credentials`.
