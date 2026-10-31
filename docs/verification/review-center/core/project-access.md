### ProjectAccess

#### Verification Approach

`ProjectAccess` is verified with unit tests defined in `ProjectAccessTests.cs`. The tests use the real
`ProjectAccess` and the real `UserService` and `ProjectService` over the in-memory fakes `FakeReviewStore` and
`FakeUserStore`, with a fixed clock and a fake password hasher, so that no database is needed. One arranged world has
two projects, `Own` and `Other`, and the users `root` and `ann` (system administrators), the project administrator
of `Own`, a user in `Own`, a user outside every project, a disabled user in `Own`, and a disabled project
administrator of `Own`. Rights are checked through the answers of the unit, and failures through the rule
carried by the `DomainRuleException`. The tests that call the membership operations of `ProjectService` live in the
same file and are described in the verification of that unit. The test double `AllowAllProjectAccess` is never used
here. The rights for the read-only role are verified with the same world plus an auditor, because the `Auditor` role
cannot be assigned through `ProjectService`; the tests store the membership directly in the fake store. The tests that
prove the call sites (`ProjectAccessGuardTests.cs`) arrange a world with a user, a project administrator, a system
administrator, and an auditor, call every operation of the review services as the auditor, and check that each
mutating one is refused with `ActorNotAllowed` and changes nothing, while the reads stay allowed. The guards of the
web pages (`GuardContributeAsync` and `GuardAuditAsync`) are verified in `ProjectViewGuardTests.cs` over the same
fakes.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Every kind of actor gets the right view, contribute, administer, audit, and system administrator answers for the
  project it belongs to and for another project.
- A missing actor, the command line, an unknown user name, a disabled user, and a project that does not exist get
  nothing, even when a membership or the administrator right is stored.
- The visible projects are all projects, only the member projects, or none, as the actor allows.
- A hidden project and a missing project fail with the same rule and the same message; a member who is no project
  administrator, and an administrator who is no system administrator, fail with the actor rule.
- A read-only member (an auditor) sees and audits the project but cannot contribute or administer; a plain member
  contributes but cannot audit; a stored role that is not defined fails closed as read-only.
- A removed membership, a lowered project role, and a disabled account change the next answer.
- An actor that came through a token has the access of its owner.
- A user with a temporary password has no access to any project, even with a membership or the system administrator
  right, until the password is changed; the change restores the access.
- Every public actor operation of the review services is classified (refused for an auditor, a read, or administer
  only), so a new operation without an access decision fails a test.
- The `Auditor` role is refused by the membership operations of `ProjectService` until it is offered.

#### Test Scenarios

**ProjectAccess_Matrix_ViewContributeAdministerAudit_EachActorKind_GetTheirRights**: A theory over the system
administrator, the project administrator, the user, the auditor, a non-member, the disabled user, the disabled project
administrator, an unknown user name, and an empty user name; each is asked to view, contribute, administer, and audit
the own project and the other project. Only the system administrator reaches both projects with every right, the
project administrator reaches every right in the own project, the user views and contributes only in the own project,
the auditor views and audits only in the own project, and the others reach nothing. This scenario is tested by
`ProjectAccess_Matrix_ViewContributeAdministerAudit_EachActorKind_GetTheirRights`.

**ProjectAccess_Matrix_ActorKinds_GetTheirRights**: A theory over the system administrator, the project
administrator, the user, a non-member, the disabled user, the disabled project administrator, an unknown user name,
and an empty user name; each is asked to view and administer the own project and the other project, and whether it is a
system administrator. Only the system administrator reaches both projects, the project administrator and the user reach
only the own project, and the others reach nothing. This scenario is tested by
`ProjectAccess_Matrix_ActorKinds_GetTheirRights`.

**ProjectAccess_Matrix_AnonymousCommandLineAndMissingProject_GetNothing**: No actor and the command line cannot see a
project, are no system administrator, and have an empty list of projects; even a system administrator cannot see or
administer a project that does not exist. This scenario is tested by
`ProjectAccess_Matrix_AnonymousCommandLineAndMissingProject_GetNothing`.

**ProjectAccess_Matrix_UndefinedRole_FailsClosedAsReadOnly**: A membership whose stored role is not a defined value can
view and audit the project but cannot contribute or administer. This scenario is tested by
`ProjectAccess_Matrix_UndefinedRole_FailsClosedAsReadOnly`.

**ProjectAccess_ListVisibleProjectsAsync_EachKind_SeesOnlyItsProjects**: The system administrator lists both
projects, the member lists only the own project, and a non-member and a disabled user list nothing. This scenario is
tested by `ProjectAccess_ListVisibleProjectsAsync_EachKind_SeesOnlyItsProjects`.

**ProjectAccess_Require_HiddenMissingAndNotAdministrator_FailWithTheRightRule**: A non-member requiring view of a
hidden project and of a missing project, and requiring administration of a hidden project, gets `ProjectNotFound`
with the same message for the first two; a member requiring administration, and a project administrator requiring the
system administrator right, get `ActorNotAllowed`; the allowed calls succeed. This scenario is tested by
`ProjectAccess_Require_HiddenMissingAndNotAdministrator_FailWithTheRightRule`.

**ProjectAccess_Require_ContributeAndAudit_FailWithTheRightRule**: A non-member requiring contribution or audit of a
hidden project and of a missing project gets `ProjectNotFound` with the same message; an auditor requiring
contribution, and a plain member requiring audit, get `ActorNotAllowed`; the allowed calls succeed. This scenario is
tested by `ProjectAccess_Require_ContributeAndAudit_FailWithTheRightRule`.

**ProjectAccess_Revocation_TakesEffectOnTheNextCall**: A membership is removed, a project administrator is made a
plain user, and a system administrator is disabled; the very next answer of each follows. This scenario is tested by
`ProjectAccess_Revocation_TakesEffectOnTheNextCall`.

**ProjectAccess_Revocation_RoleChangedBetweenCalls_ContributeAndAuditFollowTheNextCall**: A user is changed to an
auditor and back between calls; the very next contribute and audit answers follow the stored role. This scenario is
tested by `ProjectAccess_Revocation_RoleChangedBetweenCalls_ContributeAndAuditFollowTheNextCall`.

**ProjectAccess_TokenActor_HasTheMembershipsOfItsOwner**: An actor that carries the user name of a user but a display
name that names a token views the project of the owner, does not view the other project, and does not administer.
This scenario is tested by `ProjectAccess_TokenActor_HasTheMembershipsOfItsOwner`.

**ProjectService_Members_AuditorRole_IsRefusedUntilItIsOffered**: Adding a member, creating a member account, and
changing a member role with `Auditor` are refused with `ArgumentOutOfRangeException` and store nothing. This scenario
is defined in `ProjectAccessTests.cs` and tested by `ProjectService_Members_AuditorRole_IsRefusedUntilItIsOffered`.

**ReviewOperations_Auditor_AreRefusedAsNotAllowedAndChangeNothing**: An auditor calls every mutating operation of the
thread, checklist, review, planning, signing, and repository services; each is refused with `ActorNotAllowed` and
changes nothing. This scenario is defined in `ProjectAccessGuardTests.cs` and tested by
`ReviewOperations_Auditor_AreRefusedAsNotAllowedAndChangeNothing`.

**ReviewReads_Auditor_AreAllowed**: The reads of the same services work for the auditor. This scenario is defined in
`ProjectAccessGuardTests.cs` and tested by `ReviewReads_Auditor_AreAllowed`.

**ReviewOperations_ProjectAdminAndSystemAdmin_AreAllowed**: The project administrator and the system administrator can
still run the mutating operations. This scenario is defined in `ProjectAccessGuardTests.cs` and tested by
`ReviewOperations_ProjectAdminAndSystemAdmin_AreAllowed`.

**AccessCoverage_PublicActorMethods_AreAllClassified**: By reflection, every public method of the review services that
takes an actor is listed as refused for an auditor, as a read, or as administer only; an unlisted method fails the
test. This scenario is defined in `ProjectAccessGuardTests.cs` and tested by
`AccessCoverage_PublicActorMethods_AreAllClassified`.

**ProjectViewGuard_GuardContribute_Member_ReturnsTrueWithoutCallback**,
**ProjectViewGuard_GuardContribute_Auditor_ReturnsFalseWithoutCallback**,
**ProjectViewGuard_GuardContribute_NonMember_ReturnsFalseAndCallsBackOnce**, and
**ProjectViewGuard_GuardContribute_NobodySignedIn_ReturnsFalseAndCallsBackOnce**: A member may continue; a read-only
member gets `false` and no callback, so a page keeps what it showed; a non-member and a missing user get `false` and
the access-lost callback runs once. These scenarios are defined in `ProjectViewGuardTests.cs` and tested by the tests
of the same names.

**ProjectViewGuard_GuardAudit_AuditorAndProjectAdmin_ReturnTrueWithoutCallback**,
**ProjectViewGuard_GuardAudit_PlainMember_ReturnsFalseAndCallsBackOnce**, and
**ProjectViewGuard_GuardAudit_NobodySignedIn_ReturnsFalseAndCallsBackOnce**: An auditor and a project administrator
may continue; a plain member and a missing user get `false` and the callback runs once. These scenarios are defined in
`ProjectViewGuardTests.cs` and tested by the tests of the same names.

**ProjectAccess_Constructor_NullArguments_Throws**: A null user store and a null project store are rejected with an
`ArgumentNullException`. This scenario is tested by `ProjectAccess_Constructor_NullArguments_Throws`.

**ProjectAccess_TemporaryPassword_GivesNoAccessAtAll**: An account that is a project administrator and a system
administrator but has a temporary password (the must-change flag of `User`) is no system administrator, cannot view or
administer the project, lists no project, and requiring view fails with `ProjectNotFound`. The scenario is defined in
`MustChangePasswordTests.cs`, which arranges the account over `FakeReviewStore` and `FakeUserStore`, and is tested by
`ProjectAccess_TemporaryPassword_GivesNoAccessAtAll`.

**UserService_ChangeOwnPasswordAsync_NewPassword_ClearsTheFlagAndGivesAccess**: After the account chose a new password
the flag is cleared and the account views the project and lists it. This scenario, defined in
`MustChangePasswordTests.cs`, is tested by `UserService_ChangeOwnPasswordAsync_NewPassword_ClearsTheFlagAndGivesAccess`.
