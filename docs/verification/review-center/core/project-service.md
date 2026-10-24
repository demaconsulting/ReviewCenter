### ProjectService

#### Verification Approach

`ProjectService` is verified with unit tests defined in `ProjectServiceTests.cs`. The tests use the real service
with a fake in-memory store (`FakeReviewStore`) and a fixed clock (`FixedTimeProvider`), so the rules are
verified without a database and the creation time is predictable. Failures are checked through the rule carried
by the `DomainRuleException`. The blank-name test is a theory over several kinds of blank value.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A valid project is stored with its name and description trimmed and the creation time from the clock.
- A blank name and an over-long name or description are rejected with the matching rule; the limits themselves
  are accepted.
- A rejected project (blank name, over-long name, or over-long description) is never stored.
- Projects are listed ordered by name and then identity.
- A project can be read by identity, and an unknown identity gives no project.

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
