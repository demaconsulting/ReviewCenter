## Core

### Verification Approach

The `Core` subsystem is verified by subsystem tests defined in `CoreSubsystemTests.cs`. The tests use the real
`ProjectService`, `ReviewService`, `ThreadService`, `ChecklistService`, and `Fingerprint` together, with a fixed clock
and a fake
in-memory store (`FakeReviewStore`) in place of the database, because the subsystem has no dependency on a
database; the real database is verified in the `Storage` subsystem. The tests show that the units cooperate as the
design describes: the services create, store, and read back what the fingerprint algorithm and the rules require.
The rules and the algorithm themselves are verified in detail by the unit tests of the five units.

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All subsystem tests pass with zero failures.
- A project and a review created through the services are read back with the same values, the fingerprint equals
  the known answer for the files, every file keeps its own size and SHA-256 hash, and file bytes are returned
  exactly.
- The same file contents give the same fingerprint when submitted in another order or under other names.
- Every domain rule (all twenty values of `DomainRule`) is reported through the services as a domain rule
  exception that names it, and nothing is stored.
- A thread started on a file, with replies, resolved and reopened, is read back with its comments in posting order.
- A new review has the whole default checklist; an answer and a section that does not apply are recorded, and the
  progress follows them.

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
or invalid path, a file that is too large, or files that are together too large; a comment with a blank author or
text, an anchor on a missing file or beyond the end of the file, or a reply to a thread that does not exist; a
checklist answer without a name, for a missing checklist, for a check that is not in the checklist, or a failed
check without a justification). Each
fails with a `DomainRuleException` that names the rule, and the store holds only the project and review created
first. This scenario is tested by `CoreSubsystem_RuleViolation_IsReportedAsDomainRuleAndStoresNothing`.

**CoreSubsystem_ThreadWorkflow_StartReplyResolveReopen_ReadsBackInOrder**: A thread is started on lines 1 to 2 of a
file of a review, two replies are added, the thread is resolved and read back, then reopened and read back again.
The thread has the stored range, the Resolved and then the Open state, and its three comments are in posting order
with their authors, texts, and the clock time. This scenario is tested by
`CoreSubsystem_ThreadWorkflow_StartReplyResolveReopen_ReadsBackInOrder`.

**CoreSubsystem_Checklist_FrozenAtCreationAnswerAndProgress**: A review is created through the services and its
checklist is read: it has the default version and 0 of 58 checks answered. One check is then answered Fail with a
padded justification and one section is marked as not applicable with a reason. Read again, the check has the
trimmed justification, the progress is 1 of 53 (the failed check counts as answered and the skipped section leaves
the total), and the checklist is not complete. This scenario is tested by
`CoreSubsystem_Checklist_FrozenAtCreationAnswerAndProgress`.
