## Storage

### Verification Approach

The `Storage` subsystem is verified by subsystem tests defined in `StorageSubsystemTests.cs`. The tests build the
real `DatabaseSetup`, `ReviewCenterDbContext`, `ReviewStore`, and `UserStore` over a real SQLite database file, and
run the real
`ProjectService`, `ReviewService`, `ThreadService`, `ChecklistService`, and `UserService` of the `Core` subsystem over
them, so that
the subsystem is
verified in the way
the application uses it. Each test uses a new database file in a private temporary folder, supplied by the test
helper `TestDatabase`. The units are verified in detail by their own unit tests, which also use real SQLite
databases; no database substitute is used anywhere, because the behavior of the database (keys, constraints,
ordering, and binary content) is part of what is verified. The only substitute is the fast test password hasher of
the user scenario, because the strength of the real hasher is not what the subsystem proves.

### Test Environment

Tests create temporary SQLite database files in a private temporary folder, which is removed after the test run.
The native SQLite library must be loadable on the test platform (it is supplied by the provider package).

### Acceptance Criteria

- All subsystem tests pass with zero failures.
- Data written through the services is still present, byte for byte and with the same fingerprint, after the
  database is closed and opened again.
- A duplicate review identifier is refused and the original review is unchanged.
- Comment threads, their comments (in posting order), their anchors, and their state, written through the services,
  are still present after the database is closed and opened again, and each comment keeps the user name of its author.
- Users written through the user service, and a changed password, are still present after the database is closed and
  opened again; the user signs in with the changed password only.
- The checklist snapshot of a review, its answers, and its section decisions, written through the services, are
  still present after the database is closed and opened again.
- A review for a project that does not exist is refused, by the services and by the database itself, and the stored
  data is unchanged.
- An unsupported provider is refused with an error that names it, and no database is created.

### Test Scenarios

**StorageSubsystem_ServicesOverDatabase_ReopenedDatabase_KeepsProjectAndReview**: A project and a review of three
files are created through the services over a new database, which is then closed and opened again. The project,
the review with its known fingerprint and three files, and the exact bytes of the binary file are read back. This
scenario is tested by `StorageSubsystem_ServicesOverDatabase_ReopenedDatabase_KeepsProjectAndReview`.

**StorageSubsystem_ServicesOverDatabase_ReopenedDatabase_KeepsThreadsAndComments**: A review is created through the
services over a new database; a thread on line 1 is started, answered, and resolved through `ThreadService`; the
database is closed and opened again. The anchor, the state "Resolved", and both comments in posting order, each with
the user name of its author, are read
back. This scenario is tested by `StorageSubsystem_ServicesOverDatabase_ReopenedDatabase_KeepsThreadsAndComments`.

**StorageSubsystem_Users_PersistAcrossReopen**: An administrator is created through `UserService` over a new
database and its password is changed; the database is closed and opened again. The user signs in in another case of
the user name with the new password and has the Admin role, and the old password is refused. This scenario is
tested by `StorageSubsystem_Users_PersistAcrossReopen`.

**StorageSubsystem_Checklist_PersistsSnapshotAnswersAndSectionsAcrossReopen**: A review is created through the
services over a new database, one check is answered Fail with a justification, and one section is marked as not
applicable with a reason; the database is closed and opened again. The checklist is read with the default
version, the answer with its justification, the reason of the section, and the progress 1 of 53. This scenario is
tested by `StorageSubsystem_Checklist_PersistsSnapshotAnswersAndSectionsAcrossReopen`.

**StorageSubsystem_UnsupportedProvider_Opening_FailsWithClearErrorAndCreatesNothing**: A database is opened with
the provider name `Oracle` and a new file path; an `InvalidOperationException` that names the provider is thrown
and the file does not exist. This scenario is tested by
`StorageSubsystem_UnsupportedProvider_Opening_FailsWithClearErrorAndCreatesNothing`.

**StorageSubsystem_ServicesOverDatabase_DuplicateReviewId_IsRefusedAndKeepsOriginal**: A second review with the id
of an existing review in the same project is created through the services; a `DomainRuleException` for
`ReviewIdDuplicate` is thrown, and the original review still has its title and fingerprint and is the only review.
This scenario is tested by `StorageSubsystem_ServicesOverDatabase_DuplicateReviewId_IsRefusedAndKeepsOriginal`.

**StorageSubsystem_ServicesOverDatabase_UnknownProject_IsRefusedAndKeepsData**: A project with one review exists.
A review for a project id that does not exist is created through the services, and a review for that id is added
directly to the store. The service throws a `DomainRuleException` for `ProjectNotFound`, the store throws a
`DbUpdateException` because of the foreign key, no review and no project exist for the unknown id, and the existing
project and its review are unchanged. This scenario is tested by
`StorageSubsystem_ServicesOverDatabase_UnknownProject_IsRefusedAndKeepsData`.
