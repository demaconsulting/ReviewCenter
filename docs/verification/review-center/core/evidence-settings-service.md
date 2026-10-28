### EvidenceSettingsService

#### Verification Approach

`EvidenceSettingsService` and the `EvidenceSettings` type are verified with unit tests defined in
`EvidenceSettingsServiceTests.cs`. The tests use the real service over the real `ProjectAccess` and `ProjectService`
and the in-memory fakes `FakeReviewStore` and `FakeUserStore`, with a fixed clock (`MovableTimeProvider`) and the fake
password hasher (`FakePasswordHasher`), so that no database is needed. One arranged world has two projects, `Own` and
`Other`, and the users `root` (a system administrator), `pa` and `pb` (project administrators of `Own`), `mem` (a plain
member of `Own`), `out` (a user outside every project), `padm` (the project administrator of `Other`), `off` (a
disabled member of `Own`), and `offadm` (a disabled project administrator of `Own`). Rights are checked through the
rule carried by the `DomainRuleException`, and the audit entry is read from the entries that the fake store collected.
The compare-and-set itself is verified against a real SQLite database in the `ReviewStore` unit
(`EvidenceSettingsStoreTests.cs`); here the fake store implements the same contract. The actor-kinds test is a theory
over the user names of the arranged world and the null actor.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The default settings have the two anonymous switches off and the five content switches on, omit no section, and
  differ from themselves in nothing; a new project has them.
- Every content switch that is off is named as an omitted section in a fixed order, and the anonymous switches do not
  count as a reduction.
- The changes between two settings name each changed key with its old and new value in the order of the properties.
- A system administrator and a project administrator of the project read and change the settings; a plain member gets
  `ActorNotAllowed`; a user outside the project, an administrator of another project, a disabled member, a disabled
  administrator, and no actor at all get `ProjectNotFound`; a refusal changes nothing and records nothing.
- A change is stored for its own project only and is recorded as one entry `project.evidence-settings-changed` that
  carries the project and no review, names the actor, and lists each changed key as `Key off->on` or `Key on->off`.
- Saving settings that equal the expected ones changes nothing, records nothing, and reports that nothing changed.
- A change based on settings that another administrator changed first is refused with `RecordChanged`, stores nothing,
  and leaves the one entry of the first change.
- The settings of an unknown project are none, and a change to it is refused with `ProjectNotFound`.
- A null store, null access rules, and null expected or new settings are refused with an argument exception.

#### Test Scenarios

**EvidenceSettings_Default_IsPrivateAndComplete**: The default settings are read; the two anonymous switches are off
and the five content switches are on, no section is omitted, the settings are not reduced, and they differ from
themselves in nothing. This scenario is tested by `EvidenceSettings_Default_IsPrivateAndComplete`.

**EvidenceSettings_OmittedSections_NameEachSwitchThatIsOff**: Settings with the anonymous index on and every content
switch off are asked for their omitted sections; the five section names come back in the fixed order, the settings are
reduced, and settings that differ from the defaults only in an anonymous switch are not. This scenario is tested by
`EvidenceSettings_OmittedSections_NameEachSwitchThatIsOff`.

**EvidenceSettings_ChangesFrom_NamesChangedKeysWithOldAndNewValue**: Settings that turn the anonymous index on and the
reviewer names off are compared with the defaults; the two changes come back with their keys and old and new values,
in the order of the properties. This scenario is tested by
`EvidenceSettings_ChangesFrom_NamesChangedKeysWithOldAndNewValue`.

**EvidenceSettingsService_GetAsync_NewProject_HasDefaults**: A project administrator reads the settings of a project
that was never changed; they are the default settings. This scenario is tested by
`EvidenceSettingsService_GetAsync_NewProject_HasDefaults`.

**EvidenceSettingsService_GetAndSet_ActorKinds_AreAllowedOrRefused**: A theory over the system administrator, the
project administrator, the member, a user outside the project, the administrator of the other project, the disabled
member, the disabled administrator, and no actor; each reads and changes the settings of `Own`. The system
administrator and the project administrator read the defaults, change the settings, and the effective settings follow;
the member gets `ActorNotAllowed`, and the others get `ProjectNotFound`. After a refusal the effective settings are
still the defaults and no entry `project.evidence-settings-changed` exists. This scenario is tested by
`EvidenceSettingsService_GetAndSet_ActorKinds_AreAllowedOrRefused`.

**EvidenceSettingsService_SetAsync_Change_IsStoredAndAudited**: A project administrator turns the anonymous
attestations on and the threads off. The call reports `true`; the effective settings of `Own` are the new ones and
those of `Other` are still the defaults; exactly one entry `project.evidence-settings-changed` exists, with the project
of `Own`, no review, the name of the actor with the user name, and the detail
`AnonymousAttestations off->on; IncludeThreads on->off`. This scenario is tested by
`EvidenceSettingsService_SetAsync_Change_IsStoredAndAudited`.

**EvidenceSettingsService_SetAsync_NoChange_RecordsNothing**: Settings equal to the expected ones are saved; the call
reports `false` and no entry exists. This scenario is tested by
`EvidenceSettingsService_SetAsync_NoChange_RecordsNothing`.

**EvidenceSettingsService_SetAsync_ChangedMeanwhile_IsRefusedAsRecordChanged**: One administrator changes the
settings; a second, who still has the defaults as the expected settings, saves a change of another switch. The second
call raises `RecordChanged`, the stored settings are those of the first change, and only the first entry exists. This
scenario is tested by `EvidenceSettingsService_SetAsync_ChangedMeanwhile_IsRefusedAsRecordChanged`.

**EvidenceSettingsService_UnknownProject_IsNotFound**: The effective settings of a project identity that does not
exist are none, and a change by a system administrator raises `ProjectNotFound`. This scenario is tested by
`EvidenceSettingsService_UnknownProject_IsNotFound`.

**EvidenceSettingsService_NullArguments_AreRefused**: A null store, null access rules, null expected settings, and
null new settings each raise `ArgumentNullException`. This scenario is tested by
`EvidenceSettingsService_NullArguments_AreRefused`.
