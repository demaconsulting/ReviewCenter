### NotificationAddress

#### Verification Approach

`NotificationAddress` is verified with unit tests defined in `NotificationAddressTests.cs`. The unit is a pure
function, so the tests call it directly with accepted and refused texts and check the answer and the normalized
address. No stand-in and no database are needed. The refused texts include the forms that matter for security: a
second recipient hidden behind a line break, a display name, a comment, a quoted local part, an address in brackets,
and a list of addresses.

#### Test Environment

N/A - standard test environment (no special fixtures).

#### Acceptance Criteria

- All tests pass with zero failures.
- An ordinary address and an address with a plus tag are accepted, and the result is trimmed and in lower case.
- A text without an at sign, with two at signs, or with a list of addresses is refused.
- A display name, a comment, a quoted local part, and an address in brackets are refused.
- Every control character, including a line break, a tab, and a NUL, and every character outside ASCII is refused.
- A domain with one label, misplaced dots, an empty part, and a hyphen at the start or the end of a label are refused.
- An empty, white space only, or missing text is refused, and the normalized address is then empty.
- The length limits of 254 characters for the whole address and 64 characters for the local part hold at the border.

#### Test Scenarios

**NotificationAddress_TryNormalize_PlainAddress_Succeeds**: An ordinary address is accepted and returned as it is. This
scenario is tested by `NotificationAddress_TryNormalize_PlainAddress_Succeeds`.

**NotificationAddress_TryNormalize_MixedCaseWithSpaces_ReturnsTrimmedLowerCase**: An address in mixed case with white
space around it is returned trimmed and in lower case. This scenario is tested by
`NotificationAddress_TryNormalize_MixedCaseWithSpaces_ReturnsTrimmedLowerCase`.

**NotificationAddress_TryNormalize_PlusTagInLocalPart_Succeeds**: An address with a plus tag in the local part is
accepted, and `IsValid` gives the same answer. This scenario is tested by
`NotificationAddress_TryNormalize_PlusTagInLocalPart_Succeeds`.

**NotificationAddress_TryNormalize_NoAtSign_Fails**: A text without an at sign is refused and the address is empty.
This scenario is tested by `NotificationAddress_TryNormalize_NoAtSign_Fails`.

**NotificationAddress_TryNormalize_TwoAtSigns_Fails**: A text with two at signs is refused. This scenario is tested by
`NotificationAddress_TryNormalize_TwoAtSigns_Fails`.

**NotificationAddress_TryNormalize_MoreThanOneAddress_Fails**: Lists separated by a comma, a semicolon, or a space are
refused, also with padding. This scenario is tested by `NotificationAddress_TryNormalize_MoreThanOneAddress_Fails`.

**NotificationAddress_TryNormalize_NotAPlainAddress_Fails**: An address with a display name, with a comment, with a
quoted local part, or with an address in brackets is refused and the address is empty. This scenario is tested by
`NotificationAddress_TryNormalize_NotAPlainAddress_Fails`.

**NotificationAddress_TryNormalize_ControlCharacter_Fails**: An address with a line break and a second header, with
a bare line break, with a tab, or with a NUL inside is refused, also with padding, and the address is empty. This
scenario is tested by `NotificationAddress_TryNormalize_ControlCharacter_Fails`.

**NotificationAddress_TryNormalize_NonAscii_Fails**: An address with a character outside ASCII is refused. This
scenario is tested by `NotificationAddress_TryNormalize_NonAscii_Fails`.

**NotificationAddress_TryNormalize_DomainWithoutDot_Fails**: An address whose domain has one label is refused. This
scenario is tested by `NotificationAddress_TryNormalize_DomainWithoutDot_Fails`.

**NotificationAddress_TryNormalize_MalformedDots_Fails**: Addresses with a dot at the start or the end of the local
part, two dots in a row, an empty local part or domain, an empty label, or a hyphen at the border of a label are
refused, also with padding. This scenario is tested by `NotificationAddress_TryNormalize_MalformedDots_Fails`.

**NotificationAddress_TryNormalize_EmptyOrNull_Fails**: An empty text, a text of white space, and a missing text are
refused and the address is empty. This scenario is tested by `NotificationAddress_TryNormalize_EmptyOrNull_Fails`.

**NotificationAddress_TryNormalize_LengthBoundary_AcceptsMaximumAndRefusesMore**: An address of 254 characters is
accepted and one more character makes it refused. This scenario is tested by
`NotificationAddress_TryNormalize_LengthBoundary_AcceptsMaximumAndRefusesMore`.

**NotificationAddress_TryNormalize_LongLocalPart_Fails**: A local part of 65 characters is refused and one of 64
characters is accepted. This scenario is tested by `NotificationAddress_TryNormalize_LongLocalPart_Fails`.
