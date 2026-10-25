## git

This document describes the integration and usage design for the `git` OTS software item.

### Purpose

git is the source control program through which the `ContentSources` subsystem reads the source code of registered
repositories. Running the installed program gives the same behavior as the git that developers use, including its
protocol support and revision names, and avoids a git library inside the service. It is an
external runtime dependency: it is not distributed with ReviewCenter, and the operator installs it so that it is
found on the `PATH`. A version with the features listed below is required, and the `GitRunner` unit refuses an older
one with a clear error.

### Features Used

- Fetching into a bare mirror of a repository, with pruning of deleted branches, over https and from a local path
- Resolving a branch, tag, or commit name to a commit identity, and checking that a commit exists
- Listing the entries of a commit and the changes between two commits as deletions plus additions (`--no-renames`;
  renames are not detected, so a rename is one deleted file and one added file)
- Reading the exact bytes of a stored file
- Reading configuration from the environment of the process, to send a token as an HTTP header without a
  command-line argument or a stored setting

### Integration Pattern

The `GitRunner` unit is the only code that starts git. It starts the program without a shell, with a curated
environment, a time limit, and a limit on the size of the output, and it redacts secrets from every message. The
`GitSource` unit builds the commands on top of it. The tests run the real program against repositories created in a
private temporary folder, which is removed after the test run, and a local server that refuses credentials, so no
network access is needed. git has no self-validation mode; the passing content source tests are the integration
evidence.
