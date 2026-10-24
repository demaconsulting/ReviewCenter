## Core

### Verification Approach

The `Core` subsystem is verified by subsystem tests defined in `CoreSubsystemTests.cs`. The tests use the real
`ProjectService`, `ReviewService`, and `Fingerprint` together, with a fixed clock and a fake in-memory store
(`FakeReviewStore`) in place of the database, because the subsystem has no dependency on a database; the
real database is verified in the `Storage` subsystem. The tests show that the units cooperate as the design
describes: the services create, store, and read back what the fingerprint algorithm and the rules require. The
rules and the algorithm themselves are verified in detail by the unit tests of the three units.

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All subsystem tests pass with zero failures.
- A project and a review created through the services are read back with the same values, the fingerprint equals
  the known answer for the files, every file keeps its own size and SHA-256 hash, and file bytes are returned
  exactly.
- The same file contents give the same fingerprint when submitted in another order or under other names.
- Every domain rule (all twelve values of `DomainRule`) is reported through the services as a domain rule
  exception that names it, and nothing is stored.

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
or invalid path, a file that is too large, or files that are together too large). Each fails with a
`DomainRuleException` that names the rule, and the store holds only the project and review created first. This
scenario is tested by `CoreSubsystem_RuleViolation_IsReportedAsDomainRuleAndStoresNothing`.
