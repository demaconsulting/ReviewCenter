### RevisionValidator

#### Verification Approach

`RevisionValidator` is verified with tests defined in `RevisionValidatorTests.cs`. It is a pure unit, so the tests call
it
directly with revisions and check the result or the exception. Inline data lists ordinary branch, tag, and commit names
that must be accepted, and option-like and expression-like revisions that must be refused, so that every rule of the
design has at least one example.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All tests pass with zero failures.
- Ordinary branch, tag, and commit names are returned unchanged.
- A revision that is null, empty, starts with a character that is not a letter or digit (such as `-`), holds a space, a
  control character, `~`, `^`, `:`, `..`, `@{`, `//`, ends with `/`, `.`, or `.lock`, is refused with `RevisionInvalid`.
- The limit of 200 characters is inclusive.

#### Test Scenarios

**RevisionValidator_Validate_ValidRevision_ReturnsIt**: Ordinary revisions are accepted and returned unchanged. This
scenario is tested by `RevisionValidator_Validate_ValidRevision_ReturnsIt`.

**RevisionValidator_Validate_InvalidRevision_ThrowsRevisionInvalid**: Option-like and expression-like revisions are refused
with `RevisionInvalid`. This scenario is tested by `RevisionValidator_Validate_InvalidRevision_ThrowsRevisionInvalid`.

**RevisionValidator_Validate_LengthLimit_IsInclusive**: A revision of 200 characters is accepted and one of 201 is refused.
This scenario is tested by `RevisionValidator_Validate_LengthLimit_IsInclusive`.
