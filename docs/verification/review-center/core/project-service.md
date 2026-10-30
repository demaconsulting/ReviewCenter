### ProjectService

#### Verification Approach

`ProjectService` is verified with unit tests defined in `ProjectServiceTests.cs`, `ProjectAccessTests.cs`, and
`ProjectMemberServiceTests.cs`. The tests use the real service with fake in-memory stores (`FakeReviewStore`,
`FakeUserStore`, `FakeAuditStore`), the fake password hasher (`FakePasswordHasher`), and a fixed clock
(`FixedTimeProvider`), so the rules are verified without a database and the creation time is predictable. The project
rule tests run as a system administrator over the real `ProjectAccess`. The access tests arrange two projects and
users of every kind, as described for the `ProjectAccess` unit. The member management tests of
`ProjectMemberServiceTests.cs` arrange one project with a system administrator, two project administrators (one
disabled), a plain member, and outsiders, over the real `ProjectAccess` and a `MovableTimeProvider` that lets the
tests advance the throttle window; a `GatedUserStore` (over the fake user store) holds back the lookup of the names
that start with a given prefix, so the concurrency tests keep many attempts inside the lookup at the same time; the
audit entries of failed attempts are read from `FakeAuditStore` through a real
`AuditService`, and the drafts of the changes from the fake stores. Failures are checked through the rule carried by
the `DomainRuleException`. The blank-name test is a theory over several kinds of blank value, and the test of the
actor kinds is a theory over the user names of the arranged world.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A valid project is stored with its name and description trimmed and the creation time from the clock.
- Only a system administrator creates a project, and the new project has no members.
- A blank name and an over-long name or description are rejected with the matching rule; the limits themselves
  are accepted.
- A rejected project (blank name, over-long name, or over-long description) is never stored.
- Projects are listed ordered by name and then identity, and only the projects the actor may see are listed.
- A project can be read by identity, and an unknown identity or a hidden project gives no project.
- A system administrator and a project administrator of the project add, re-role, and remove members with audit
  entries that carry the project; a plain member is refused with `ActorNotAllowed` and an actor who cannot see the
  project gets `ProjectNotFound`; a project administrator lists the members of the own project only.
- A repeated add, an unknown membership, an unknown user, and an unknown project are refused with the matching rule.
- A member is added only by the exact user name; an unknown name and a disabled user give one rule and one message,
  and an unknown name and a non-member give one rule and one message for a removal or role change.
- After ten failed attempts within ten minutes an actor who is no system administrator is refused with
  `TooManyAttempts`, other actors are not, the actor may try again when the window has passed, and a system
  administrator is never refused; every failed attempt is audited with the project and the typed name.
- Attempts made at the same time cannot exceed the limit: with the lookups held back, exactly ten attempts of one
  actor are admitted and the rest are refused; a system administrator is never limited; an attempt whose lookup throws
  stays counted; a success gives its reservation back.
- The last enabled project administrator cannot be removed or demoted by a project administrator (itself included),
  a disabled administrator does not count, a system administrator is not blocked, and a refusal changes and audits
  nothing.
- An account created for a project is no system administrator, has a random temporary password of sixteen characters
  that is stored only as a hash and never audited, and must be changed; a taken or invalid name creates no account and
  no membership.
- A new project requires independent reviewers by default. Only a project administrator (or a system administrator)
  switches the rule, with the value the caller read as the expected value; the change and the audit entry
  `project.independent-reviewers-changed` (detail `RequireIndependentReviewers on->off`) are stored together. A plain
  member, a user who is no member, and an actor that came through a token are refused with `ActorNotAllowed`
  (`ProjectNotFound` for a project that is hidden), and a call whose expected value does not match the stored one
  changes and audits nothing.

#### Test Scenarios

**ProjectService_CreateAsync_ValidInput_StoresTrimmedProject**: A project with surrounding white space in the name
and description is created; the stored project has the trimmed values and the clock time. This scenario is tested
by `ProjectService_CreateAsync_ValidInput_StoresTrimmedProject`.

**ProjectService_CreateAsync_NullDescription_StoresEmptyDescription**: A project is created without a
description; the stored description is empty. This scenario is tested by
`ProjectService_CreateAsync_NullDescription_StoresEmptyDescription`.

**ProjectService_CreateAsync_BlankName_ThrowsProjectNameInvalid**: Null, empty, and white-space names are
rejected with `ProjectNameInvalid`. This scenario is tested by
`ProjectService_CreateAsync_BlankName_ThrowsProjectNameInvalid`.

**ProjectService_CreateAsync_NameLength_LimitIsInclusive**: A name of exactly the maximum length is accepted and
a name one character longer is rejected with `ProjectNameInvalid`. This scenario is tested by
`ProjectService_CreateAsync_NameLength_LimitIsInclusive`.

**ProjectService_CreateAsync_DescriptionTooLong_ThrowsDescriptionTooLong**: A description longer than the maximum
is rejected with `ProjectDescriptionTooLong`. This scenario is tested by
`ProjectService_CreateAsync_DescriptionTooLong_ThrowsDescriptionTooLong`.

**ProjectService_CreateAsync_Rejected_StoresNothing**: A theory over a blank name, an over-long name, and an
over-long description; after each rejected creation the store holds no project. This scenario is tested by
`ProjectService_CreateAsync_Rejected_StoresNothing`.

**ProjectService_ListAsync_SeveralProjects_ReturnsOrderedByNameThenId**: Projects are created out of name order,
two of them with the same name; they are listed by name, and by identity within the same name. This scenario is
tested by `ProjectService_ListAsync_SeveralProjects_ReturnsOrderedByNameThenId`.

**ProjectService_GetAsync_ExistingAndUnknown_ReturnsProjectOrNull**: An existing project is returned by its
identity and an unknown identity returns null. This scenario is tested by
`ProjectService_GetAsync_ExistingAndUnknown_ReturnsProjectOrNull`.

**ProjectService_CreateAsync_Project_RecordsProjectCreated**: A project is created by a named actor; the audit entry
`project.created` is recorded with the actor and the project name in the detail. This scenario is tested by
`ProjectService_CreateAsync_Project_RecordsProjectCreated`, which is defined in `AuditEventTests.cs`.

**ProjectService_CreateAsync_OnlySystemAdmin_NewProjectHasNoMembers**: A project administrator, a plain user, a
non-member, and a disabled user are each refused with `ActorNotAllowed`; the system administrator creates the project,
and it has no members. This scenario is tested by `ProjectService_CreateAsync_OnlySystemAdmin_NewProjectHasNoMembers`.

**ProjectService_Members_SystemAdminManages_OthersAreRefused**: A system administrator adds the non-member, promotes
the new member to project administrator, lists the members (ordered by user name), and removes the member again; the
audit entries `project.member-added`, `project.member-role-changed`, and `project.member-removed` are recorded in this
order and carry the project. A plain member is refused each of the three changes with `ActorNotAllowed`, and an
outsider with `ProjectNotFound`. This scenario is tested by
`ProjectService_Members_SystemAdminManages_OthersAreRefused`.

**ProjectService_MemberActions_ActorKinds_AreAllowedOrRefused**: A theory over a system administrator, a project
administrator of the project, a plain member, an outsider, and a project administrator of another project; each of
the actions add, remove, change role, create account, and list runs on a fresh arrangement. The first two succeed, the
plain member is refused with `ActorNotAllowed`, and the last two with `ProjectNotFound`; a refusal leaves the
memberships unchanged and creates no account. This scenario is tested by
`ProjectService_MemberActions_ActorKinds_AreAllowedOrRefused`.

**ProjectService_AddMemberAsync_UnknownAndDisabledName_AreIndistinguishable**: A name that does not exist and the name
of a disabled user are refused with the same rule and the same message, which does not repeat the typed name; a
partial name finds nothing; the exact name in another letter case, with surrounding white space, is added. This
scenario is tested by `ProjectService_AddMemberAsync_UnknownAndDisabledName_AreIndistinguishable`.

**ProjectService_RemoveAndSetRole_UnknownNameAndNonMember_AreIndistinguishable**: Removing and changing the role of an unknown
name and a user who is no member of the project are all refused with `MembershipNotFound` and one single message.
This scenario is tested by `ProjectService_RemoveAndSetRole_UnknownNameAndNonMember_AreIndistinguishable`.

**ProjectService_AddMemberAsync_TooManyFailures_ThrottlesPerActorUntilTheWindowEnds**: A project administrator fails
ten times; the eleventh add (even of an existing name) and an account creation are refused with `TooManyAttempts`; a
second project administrator is not refused; a system administrator fails twelve times and is never refused. After
the clock advances by the throttle window the first actor adds the member. This scenario is tested by
`ProjectService_AddMemberAsync_TooManyFailures_ThrottlesPerActorUntilTheWindowEnds`.

**ProjectService_AddMemberAsync_ConcurrentAttempts_AreAdmittedUpToTheLimitOnly**: Twenty-five attempts of one project
administrator run at the same time, while the user lookup of every admitted attempt is held back by a gated user store.
The five attempts over the limit are refused with `TooManyAttempts` before they reach the lookup; exactly ten reach the
lookup. After the gate opens, those ten fail with `UserNotFound` and exactly ten failures are audited. This scenario
is tested by `ProjectService_AddMemberAsync_ConcurrentAttempts_AreAdmittedUpToTheLimitOnly`.

**ProjectService_AddMemberAsync_ConcurrentAttemptsOfSystemAdministrator_AreNeverThrottled**: Twenty-five attempts of a
system administrator run at the same time; all of them reach the held lookup and none is refused for the throttle.
This scenario is tested by
`ProjectService_AddMemberAsync_ConcurrentAttemptsOfSystemAdministrator_AreNeverThrottled`.

**ProjectService_AddMemberAsync_LookupThrows_KeepsTheAttemptCounted**: The user lookup throws ten times; the eleventh
attempt is refused with `TooManyAttempts`, so an exception keeps the reservation counted. This scenario is tested by
`ProjectService_AddMemberAsync_LookupThrows_KeepsTheAttemptCounted`.

**ProjectService_CreateMemberAccountAsync_Successes_DoNotCountAgainstTheLimit**: After nine failed adds, fifteen created
accounts all succeed, an invalid user name is refused without counting, one more failed add is still allowed, and the
next add is refused with `TooManyAttempts`; a success gives its reservation back. This scenario is tested by
`ProjectService_CreateMemberAccountAsync_Successes_DoNotCountAgainstTheLimit`.

**ProjectService_AddMemberAsync_FailedAttempts_AreAuditedWithTheProject**: A failure of a project administrator, a
failure of a system administrator, and then a success; exactly two `project.member-add-failed` entries exist, each
with the project and the typed name in the detail, and the success writes none. This scenario is tested by
`ProjectService_AddMemberAsync_FailedAttempts_AreAuditedWithTheProject`.

**ProjectService_RemoveAndDemote_LastProjectAdmin_IsRefusedForAProjectAdmin**: The only enabled project administrator
removes or demotes themselves and is refused with `LastProjectAdmin` and an explanation; nothing is changed or audited.
After a second administrator is added, the first demotes themselves, and the second is then refused when removing
themselves. This scenario is tested by `ProjectService_RemoveAndDemote_LastProjectAdmin_IsRefusedForAProjectAdmin`.

**ProjectService_RemoveMemberAsync_SystemAdminAndDisabledTarget_AreNotBlocked**: A project administrator removes a
disabled administrator, is refused when removing themselves as the only enabled administrator, and a system
administrator then removes that last administrator. This scenario is tested by
`ProjectService_RemoveMemberAsync_SystemAdminAndDisabledTarget_AreNotBlocked`.

**ProjectService_CreateMemberAccountAsync_ProjectAdmin_CreatesNoSystemAdministrator**: A project administrator creates
an account as a project administrator; the stored user is enabled, is no system administrator, and has the membership
with the role. This scenario is tested by
`ProjectService_CreateMemberAccountAsync_ProjectAdmin_CreatesNoSystemAdministrator`.

**ProjectService_CreateMemberAccountAsync_TemporaryPassword_IsRandomHashedAndNeverAudited**: Two accounts are created;
the passwords have sixteen characters and differ, the stored value is a hash that verifies the password, the must-change
flag is set, the role is stored, a `project.account-created` entry with the project exists for each, and no audit
draft contains a password. This scenario is tested by
`ProjectService_CreateMemberAccountAsync_TemporaryPassword_IsRandomHashedAndNeverAudited`.

**ProjectService_CreateMemberAccountAsync_TakenAndInvalidInput_CreatesNothing**: A taken name (in another letter
case), an invalid user name, and a blank display name are refused with `UserNameTaken`, `UserNameInvalid`, and
`DisplayNameInvalid`; no account or membership is created and only the taken name is audited as a failed attempt. This
scenario is tested by `ProjectService_CreateMemberAccountAsync_TakenAndInvalidInput_CreatesNothing`.

**NewMemberAccount_ToString_DoesNotContainTemporaryPassword**: The text of a new account, also when interpolated into a
string, and the text of a really created account do not contain the temporary password, and the text still names the
member. This scenario is tested by `NewMemberAccount_ToString_DoesNotContainTemporaryPassword`.

**ProjectService_Constructor_NullArguments_Throws**: Each required constructor argument set to null throws an
`ArgumentNullException`. This scenario is tested by `ProjectService_Constructor_NullArguments_Throws`.

**ProjectService_ListMembersAsync_ProjectAdminOwnProjectOnly**: A project administrator lists the members of the own
project ordered by user name; for another project the answer is `ProjectNotFound`, and a plain member is refused with
`ActorNotAllowed`. This scenario is tested by `ProjectService_ListMembersAsync_ProjectAdminOwnProjectOnly`.

**ProjectService_Members_UnknownDoubledAndMissing_ViolateTheirRules**: Adding an unknown user gives `UserNotFound`;
adding to an unknown project gives `ProjectNotFound`; adding a member twice gives `MembershipExists`; removing a user
who is no member gives `MembershipNotFound`; an undefined role is rejected with an `ArgumentOutOfRangeException`. This
scenario is tested by `ProjectService_Members_UnknownDoubledAndMissing_ViolateTheirRules`.

**ProjectService_GetAsync_HiddenProject_ReadsAsMissing**: A member reads the own project; the same member reading
another project, a non-member reading the project, and a system administrator reading a project that does not exist
all get null; the project list of the member holds only the own project. This scenario is tested by
`ProjectService_GetAsync_HiddenProject_ReadsAsMissing`.

**ProjectService_SetRequireIndependentReviewersAsync_DefaultIsOn**: A project that is created through the service
requires independent reviewers. This scenario is tested by `ProjectService_SetRequireIndependentReviewersAsync_DefaultIsOn`.

**ProjectService_SetRequireIndependentReviewersAsync_Admin_TogglesAndAudits**: An administrator switches the rule off
and on again; the project reads back each value, the call returns `true`, and the audit trail holds an entry
`project.independent-reviewers-changed` with the project and the details `RequireIndependentReviewers on->off` and
`RequireIndependentReviewers off->on`. This scenario is tested by
`ProjectService_SetRequireIndependentReviewersAsync_Admin_TogglesAndAudits`.

**ProjectService_SetRequireIndependentReviewersAsync_NotAdminOrApiToken_IsRefused**: A plain member of the project and
an administrator whose identity arrives through a token are refused with `ActorNotAllowed`; the value is unchanged and
no entry is audited. This scenario is tested by
`ProjectService_SetRequireIndependentReviewersAsync_NotAdminOrApiToken_IsRefused`.

**ProjectService_SetRequireIndependentReviewersAsync_StaleExpected_ThrowsRecordChanged**: A change whose expected value
is no longer the stored one is refused with `RecordChanged`, does not overwrite the newer value, and adds no audit
entry. This scenario is tested by `ProjectService_SetRequireIndependentReviewersAsync_StaleExpected_ThrowsRecordChanged`.

The tests of the membership operations, of the system administrator rule for creation, and of the hidden project are
defined in `ProjectAccessTests.cs`, which arranges the users they need.
