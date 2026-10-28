### ProjectService

#### Verification Approach

`ProjectService` is verified with unit tests defined in `ProjectServiceTests.cs` and `ProjectAccessTests.cs`. The tests
use the real service with fake in-memory stores (`FakeReviewStore`, `FakeUserStore`) and a fixed clock
(`FixedTimeProvider`), so the rules are verified without a database and the creation time is predictable. The project
rule tests run as a system administrator over the real `ProjectAccess`. The access tests arrange two projects and
users of every kind, as described for the `ProjectAccess` unit. Failures are checked through the rule carried by the
`DomainRuleException`. The blank-name test is a theory over several kinds of blank value.

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
- A system administrator adds, re-roles, and removes members with audit entries that carry the project; other actors
  are refused; a project administrator lists the members of the own project only.
- A repeated add, an unknown membership, an unknown user, and an unknown project are refused with the matching rule.

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
order and carry the project. A project administrator, a plain user, and a non-member are refused each of the three
changes with `ActorNotAllowed`. This scenario is tested by
`ProjectService_Members_SystemAdminManages_OthersAreRefused`.

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

The tests of the membership operations, of the system administrator rule for creation, and of the hidden project are
defined in `ProjectAccessTests.cs`, which arranges the users they need.
