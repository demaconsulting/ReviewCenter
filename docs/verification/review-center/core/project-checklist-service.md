### ProjectChecklistService

#### Verification Approach

`ProjectChecklistService` is verified with unit tests defined in `ProjectChecklistServiceTests.cs`. The tests use the real
service over the real `ProjectAccess` and `ProjectService` and the in-memory fakes `FakeReviewStore` and `FakeUserStore`,
with a fixed clock (`MovableTimeProvider`) and the fake password hasher (`FakePasswordHasher`), so that no database is
needed. One arranged world has two projects, `Own` and `Other`, and the users `root` (a system administrator), `pa` (a
project administrator of `Own`), `mem` (a plain member of `Own`), `out` (a user outside every project), `padm` (the
project administrator of `Other`), `off` (a disabled member of `Own`), and `offadm` (a disabled project administrator of
`Own`). Rights are checked through the rule carried by the `DomainRuleException`, and the audit entry is read from the
entries that the fake store collected. The actor-kinds test is a theory over the user names of the arranged world and the
null actor. The strict validation is verified in the `ChecklistService` unit (`ChecklistValidatorTests.cs`).

The compare-and-set itself, the storage of the text, and the race of two changes that start from the same hash are
verified against a real SQLite database in the `ReviewStore` unit (`ProjectChecklistStoreTests.cs`); the race test is a
theory over the store and the service, so the service requirement for exactly one winner is verified through the real
database. That a new review freezes the definition of its project, and is not affected by a later change, is verified in
the `ReviewService` unit (`ReviewServiceChecklistTests.cs`).

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A project without a definition reads as the built-in default, with its text, its hash, and 14 sections and 58 checks;
  the hash is the SHA-256 of the text, and for no stored text it is the hash of the default's text.
- A definition that fails the validation is refused with `ChecklistDefinitionInvalid`, with every error and its path in
  the message, and nothing is stored or recorded; the pure check reports the same errors and stores nothing.
- A system administrator and a project administrator of the project read and change the checklist; a plain member gets
  `ActorNotAllowed`; a user outside the project, an administrator of another project, a disabled member, a disabled
  administrator, and no actor at all get `ProjectNotFound`; a refusal changes nothing and records nothing.
- A change, a replacement or a reset, that comes through an API token is refused with `ActorNotAllowed`; a read through
  an API token is allowed for a project the token's user may see.
- A change is stored in its normal form for its own project only and is recorded as one entry `project.checklist-changed`
  that carries the project and no review, names the actor, states the version, the number of sections and items, a short
  hash, and the previous version, never contains the wording, and is within the length limit of a detail.
- A replacement with other content but the same version label as the current definition, or as the default, is refused
  with `ChecklistDefinitionInvalid`; with a new label it succeeds.
- A replacement with the same content as the current definition, and a reset of a project that uses the default, change
  nothing, record nothing, and report that nothing changed.
- A reset removes the definition, so the project reads as the default again, and records `project.checklist-changed`
  with the detail that the checklist was reset to the default.
- A change or a reset that is based on a hash that is no longer the stored one is refused with `RecordChanged`, stores
  nothing, and leaves the one entry of the first change; of two simultaneous changes from the same hash exactly one
  succeeds.
- An unknown project is `ProjectNotFound` for a read and for a change.
- A null store, null access rules, and a null actor or expected hash for a change are refused with an argument exception.

#### Test Scenarios

**ProjectChecklistService_GetAsync_NewProject_IsDefault**: A project administrator reads the checklist of a project that
was never changed; it is the default, with the default's version, text, and hash, 14 sections, and 58 items. This
scenario is tested by `ProjectChecklistService_GetAsync_NewProject_IsDefault`.

**ProjectChecklistService_HashOf_IsSha256OfTheTextAndOfTheDefaultForNull**: The hash of a text is compared with the
SHA-256 of its UTF-8 bytes, and the hash for no text is the hash of the default's text. This scenario is tested by
`ProjectChecklistService_HashOf_IsSha256OfTheTextAndOfTheDefaultForNull`.

**ProjectChecklistService_GetAndSet_ActorKinds_AreAllowedOrRefused**: A theory over the system administrator, the project
administrator, the member, a user outside the project, the administrator of the other project, the disabled member, the
disabled administrator, and no actor; each reads and replaces the checklist of `Own`. The system administrator and the
project administrator read the default and replace it, and the new version is then read back; the member can read it but
gets `ActorNotAllowed` for the change, and the others get `ProjectNotFound` for both. After a refusal the project still
has no definition and no entry `project.checklist-changed` exists. This scenario is tested by
`ProjectChecklistService_GetAndSet_ActorKinds_AreAllowedOrRefused`.

**ProjectChecklistService_SetAndReset_ApiToken_AreRefused**: A project administrator who came through an API token
replaces and resets the checklist; both raise `ActorNotAllowed`, the project still has no definition, and no entry exists;
reading the checklist with the same token succeeds.
This scenario is tested by `ProjectChecklistService_SetAndReset_ApiToken_AreRefused`.

**ProjectChecklistService_SetAsync_Change_IsStoredAndAudited**: A project administrator replaces the default with a
one-section definition that has the label `moon-1`. The call reports `true`; the checklist of `Own` has that version, text,
hash, and one section with one item, and that of `Other` is still the default; exactly one entry
`project.checklist-changed` exists, with the project of `Own`, no review, the name of the actor, and the detail with the
version, the counts, the short hash, and the previous version, that does not contain the wording and is within the limit.
This scenario is tested by `ProjectChecklistService_SetAsync_Change_IsStoredAndAudited`.

**ProjectChecklistService_SetAsync_InvalidDefinition_IsRefusedWithAllErrors**: A definition with an empty
section title and a duplicate identifier is saved; the call raises `ChecklistDefinitionInvalid`, the message names both
paths, and nothing is stored or recorded. This scenario is tested by
`ProjectChecklistService_SetAsync_InvalidDefinition_IsRefusedWithAllErrors`.

**ProjectChecklistService_SetAsync_NormalFormOverLimit_IsRefused**: A definition of 200 checks of 1000 `<` characters is
under 256 KiB as sent but over it once written in its normal form; the replacement raises `ChecklistDefinitionInvalid`
and nothing is stored or recorded. This scenario is tested by `ProjectChecklistService_SetAsync_NormalFormOverLimit_IsRefused`.

**ProjectChecklistService_Validate_ReportsAllErrorsAndStoresNothing**: The pure check is given a definition with a missing
version and no sections, and a valid one; the first reports both errors with their paths, the second is valid, and the
project has no definition and no entry afterwards. This scenario is tested by
`ProjectChecklistService_Validate_ReportsAllErrorsAndStoresNothing`.

**ProjectChecklistService_SetAsync_SameVersionOtherContent_IsRefused**: A project administrator replaces a stored
definition with other wording under the same label; the call raises `ChecklistDefinitionInvalid` with a message that says
the label must change; with a new label the replacement succeeds and two entries exist. This scenario is tested by
`ProjectChecklistService_SetAsync_SameVersionOtherContent_IsRefused`.

**ProjectChecklistService_SetAsync_DefaultLabelOtherContent_IsRefused**: A definition with the label of the default but
other wording is saved for a project that uses the default; the call raises `ChecklistDefinitionInvalid`. This scenario
is tested by `ProjectChecklistService_SetAsync_DefaultLabelOtherContent_IsRefused`.

**ProjectChecklistService_SetAsync_SameContent_RecordsNothing**: The default's own text is saved for a project that uses
the default, and a stored definition is saved again unchanged; both report `false` and only the first real change has an
entry. This scenario is tested by `ProjectChecklistService_SetAsync_SameContent_RecordsNothing`.

**ProjectChecklistService_ResetAsync_AlreadyDefault_RecordsNothing**: A project that uses the default is reset; the call
reports `false` and no entry exists. This scenario is tested by
`ProjectChecklistService_ResetAsync_AlreadyDefault_RecordsNothing`.

**ProjectChecklistService_ResetAsync_CustomDefinition_RestoresDefaultAndAudits**: A project administrator resets a project
with a stored definition; the call reports `true`, the project has no definition and reads as the default, and the entry
`project.checklist-changed` says that the checklist was reset to the default, with the default's version and the previous
version. This scenario is tested by `ProjectChecklistService_ResetAsync_CustomDefinition_RestoresDefaultAndAudits`.

**ProjectChecklistService_SetAndReset_ChangedMeanwhile_AreRefusedAsRecordChanged**: One administrator replaces the
checklist; a second, who still has the hash of the default, replaces and resets it. Both calls raise `RecordChanged`, the
stored definition is that of the first change, and only the first entry exists. This scenario is tested by
`ProjectChecklistService_SetAndReset_ChangedMeanwhile_AreRefusedAsRecordChanged`.

**ReviewStore_UpdateChecklistDefinitionAsync_ConcurrentWriters_ExactlyOneWins**: Two changes that start from the same hash
are started at the same time against a real database, once through the store and once through the service; exactly one
succeeds and the other reports that the definition changed, one entry exists, and the chain verifies. This scenario is
tested by `ReviewStore_UpdateChecklistDefinitionAsync_ConcurrentWriters_ExactlyOneWins`.

**ProjectChecklistService_UnknownProject_IsNotFound**: A read, a replacement, and a reset of a project identity that does
not exist by a system administrator raise `ProjectNotFound`. This scenario is tested by
`ProjectChecklistService_UnknownProject_IsNotFound`.

**ProjectChecklistService_NullArguments_AreRefused**: A null store, null access rules, and a null actor or expected hash
for a replacement and a reset each raise `ArgumentNullException`. This scenario is tested by
`ProjectChecklistService_NullArguments_AreRefused`.
