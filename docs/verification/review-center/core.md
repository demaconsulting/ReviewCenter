## Core

### Verification Approach

The `Core` subsystem is verified by subsystem tests defined in `CoreSubsystemTests.cs`. The tests use the real
`ProjectService`, `ReviewService`, `ThreadService`, `ChecklistService`, `UserService`, `SigningService`, `Fingerprint`,
and `RecordDigest` together, with
a fixed clock (a clock the test can move for the lockout) and fakes for the stores (`FakeReviewStore`,
`FakeUserStore`) and a trivial password hasher (`FakePasswordHasher`) in place of the database and the real hasher,
because the subsystem has no dependency on a
database; the real database is verified in the `Storage` subsystem and the real hasher in the `Identity` subsystem.
The tests show that the units cooperate as the
design describes: the services create, store, and read back what the fingerprint algorithm and the rules require.
The rules and the algorithm themselves are verified in detail by the unit tests of the eight units.

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All subsystem tests pass with zero failures.
- A project and a review created through the services are read back with the same values, the fingerprint equals
  the known answer for the files, every file keeps its own size and SHA-256 hash, and file bytes are returned
  exactly.
- The same file contents give the same fingerprint when submitted in another order or under other names.
- Every domain rule (all thirty-three values of `DomainRule`) is reported through the services as a domain rule
  exception that names it, and nothing is stored.
- A thread started on a file, with replies, resolved and reopened, is read back with its comments in posting order.
- A new review has the whole default checklist; an answer and a section that does not apply are recorded, and the
  progress follows them.
- A user is created and signs in regardless of the case of the user name; repeated failures lock the account and the
  lockout ends on time; a changed password replaces the old one; and the last enabled administrator cannot be
  demoted.
- A review with a reviewer slot, answered completely and without open threads, is signed with the password; the
  signature makes the review approved, a later change of the record makes it out of date and not approved, and the
  slot can be signed again.
- Every change made through the services is recorded in the audit trail with the person, the action, and a detail
  that never holds a password; recorded entries verify as an intact chain.

### Test Scenarios

**CoreSubsystem_ProjectAndReviewWorkflow_ValidInput_StoresFingerprintsAndReadsBack**: A project is created, a
review of three files (text, text, and binary with carriage returns) is created in it, and both are read back
through the services. The review has the Draft state, the known fingerprint, and three files, each with its own
size and known SHA-256 hash, and the exact bytes of the binary file can be read. This scenario is tested by
`CoreSubsystem_ProjectAndReviewWorkflow_ValidInput_StoresFingerprintsAndReadsBack`.

**CoreSubsystem_Fingerprint_SameContentOtherNamesAndOrder_GivesSameFingerprint**: Two reviews are created in one
project from the same three file contents, the second in reverse order and under other file names. Both reviews
have the known fingerprint of the contents. This scenario is tested by
`CoreSubsystem_Fingerprint_SameContentOtherNamesAndOrder_GivesSameFingerprint`.

**CoreSubsystem_RuleViolation_IsReportedAsDomainRuleAndStoresNothing**: A theory over every value of
`DomainRule`, so that a rule added later without a scenario fails the test. For each rule a request that breaks
it is made through the services (a project with a blank name or a description that is too long; a review for a
missing project, with an invalid or duplicate identifier, an invalid title, no files, too many files, a duplicate
or invalid path, a file that is too large, or files that are together too large; a comment with a blank text, an
anchor on a missing file or beyond the end of the file, or a reply to a thread that does not exist; a
checklist answer for a missing checklist, for a check that is not in the checklist, or a failed
check without a justification; a user with an invalid or taken user name, a blank display name, or a too short
password, a wrong current password, a change of a user that does not exist, or disabling the last enabled
administrator; a slot for a missing review, a slot with an unknown reviewer or one that does not exist, a change of a
signed slot, a signing by a user who is not in the slot, a signing that is blocked, a wrong password at a signing, or
a signing of a record that changed). Each
fails with a `DomainRuleException` that names the rule, and the store holds only the project, review, and
administrator created
first. This scenario is tested by `CoreSubsystem_RuleViolation_IsReportedAsDomainRuleAndStoresNothing`.

**CoreSubsystem_ThreadWorkflow_StartReplyResolveReopen_ReadsBackInOrder**: A thread is started on lines 1 to 2 of a
file of a review, two replies are added, the thread is resolved and read back, then reopened and read back again.
The thread has the stored range, the Resolved and then the Open state, and its three comments are in posting order
with their authors (the display names of the actors), texts, and the clock time. This scenario is tested by
`CoreSubsystem_ThreadWorkflow_StartReplyResolveReopen_ReadsBackInOrder`.

**CoreSubsystem_Checklist_FrozenAtCreationAnswerAndProgress**: A review is created through the services and its
checklist is read: it has the default version and 0 of 58 checks answered. One check is then answered Fail with a
padded justification and one section is marked as not applicable with a reason. Read again, the check has the
trimmed justification, the progress is 1 of 53 (the failed check counts as answered and the skipped section leaves
the total), and the checklist is not complete. This scenario is tested by
`CoreSubsystem_Checklist_FrozenAtCreationAnswerAndProgress`.

**CoreSubsystem_UsersWorkflow_CreateSignInLockoutAndLastAdmin**: An administrator and a reviewer are created, and the
reviewer signs in with the user name in another case. Five wrong passwords then lock the account, so that the right
password is refused; after five minutes on the movable clock the right password signs in again. The password is
changed with the current one and the new one works. Demoting the only administrator is refused with `LastAdmin`.
This scenario is tested by `CoreSubsystem_UsersWorkflow_CreateSignInLockoutAndLastAdmin`.

**CoreSubsystem_Signatures_SignChangeOutOfDateAndResign_FollowsTheRecord**: A review has one slot of two users. Its
status is blocked while the checks are unanswered; after all checks are answered it has no blockers. Signing with a
wrong password is refused with `ReauthenticationFailed`; signing with the right one approves the review. A thread is
then opened and resolved, which changes the record: the slot is out of date, the review is not approved, and the
digest differs. The alternate signs the new record, and the review is approved again with the alternate as signer.
This scenario is tested by `CoreSubsystem_Signatures_SignChangeOutOfDateAndResign_FollowsTheRecord`.

**Audit trail of the services**: The unit tests of `AuditService` and `UserService` verify the subsystem requirement
for the audit trail. A draft recorded through `AuditService` is stored and the chain verifies, which is tested by
`AuditService_RecordAsync_Draft_StoresEntryThatVerifies`, and the administration events of `UserService` (creation,
enabling, disabling, role change, and password changes) are recorded with the acting person and without a password,
which is tested by `UserService_AdministrationEvents_AreRecordedWithoutPasswords`. The domain services are tested
against a fake store that holds the entries in memory; the transaction that stores a change with its entry is verified
in the `Storage` subsystem.
