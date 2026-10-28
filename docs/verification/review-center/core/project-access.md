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
here.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Every kind of actor gets the right view, administer, and system administrator answers for the project it belongs to
  and for another project.
- A missing actor, the command line, an unknown user name, a disabled user, and a project that does not exist get
  nothing, even when a membership or the administrator right is stored.
- The visible projects are all projects, only the member projects, or none, as the actor allows.
- A hidden project and a missing project fail with the same rule and the same message; a member who is no project
  administrator, and an administrator who is no system administrator, fail with the actor rule.
- A removed membership, a lowered project role, and a disabled account change the next answer.
- An actor that came through a token has the access of its owner.
- A user with a temporary password has no access to any project, even with a membership or the system administrator
  right, until the password is changed; the change restores the access.

#### Test Scenarios

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

**ProjectAccess_ListVisibleProjectsAsync_EachKind_SeesOnlyItsProjects**: The system administrator lists both
projects, the member lists only the own project, and a non-member and a disabled user list nothing. This scenario is
tested by `ProjectAccess_ListVisibleProjectsAsync_EachKind_SeesOnlyItsProjects`.

**ProjectAccess_Require_HiddenMissingAndNotAdministrator_FailWithTheRightRule**: A non-member requiring view of a
hidden project and of a missing project, and requiring administration of a hidden project, gets `ProjectNotFound`
with the same message for the first two; a member requiring administration, and a project administrator requiring the
system administrator right, get `ActorNotAllowed`; the allowed calls succeed. This scenario is tested by
`ProjectAccess_Require_HiddenMissingAndNotAdministrator_FailWithTheRightRule`.

**ProjectAccess_Revocation_TakesEffectOnTheNextCall**: A membership is removed, a project administrator is made a
plain user, and a system administrator is disabled; the very next answer of each follows. This scenario is tested by
`ProjectAccess_Revocation_TakesEffectOnTheNextCall`.

**ProjectAccess_TokenActor_HasTheMembershipsOfItsOwner**: An actor that carries the user name of a user but a display
name that names a token views the project of the owner, does not view the other project, and does not administer.
This scenario is tested by `ProjectAccess_TokenActor_HasTheMembershipsOfItsOwner`.

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
