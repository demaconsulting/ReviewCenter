### SelectionMatcher

#### Verification Approach

`SelectionMatcher` is verified with tests defined in `SelectionMatcherTests.cs`. It is a pure unit, so the tests call
it directly with patterns and paths and check the answer. Tables of inline data cover the syntax: literal paths,
folders, wildcards, and exclusions. Separate tests check the edge cases that matter for safety: the trailing `**`, the
star that must not cross a separator, a pattern with many `**` segments that must finish in bounded time, the
normalization of lines, the invalid inputs, and the inclusive limits. A differential test compares the unit with
`Microsoft.Extensions.FileSystemGlobbing`, the library that ReviewMark uses, over a corpus of the syntax that both
support, so that the documented differences are the only ones.

#### Test Environment

N/A - standard test environment (the globbing library is a package of the test project only).

#### Acceptance Criteria

- All tests pass with zero failures.
- The last matching pattern decides; an exclusion removes a path and a later include adds it back; a path that no pattern
  matches is not selected.
- A trailing `**` needs at least one more segment, and a trailing `/` selects a folder.
- `*` does not cross `/`, `**` as a segment does, and `?` matches exactly one character; matching is case-sensitive.
- A pattern with many `**` segments is matched in bounded time.
- Lines are trimmed and blank lines skipped; a leading `./` or `/` is dropped.
- A selection with no pattern, a `\`, an empty, `.`, or `..` segment, a control character, an empty exclusion, a line
  over 400 characters, or more than 100 patterns is `SelectionInvalid`; 400 characters and 100 patterns are accepted.
- Null arguments are refused.
- For the common syntax the unit agrees with the globbing library.

#### Test Scenarios

**SelectionMatcher_IsMatch_Patterns_SelectExpectedPaths**: Literals, folders, wildcards, and exclusions select the expected
paths. This scenario is tested by `SelectionMatcher_IsMatch_Patterns_SelectExpectedPaths`.

**SelectionMatcher_IsMatch_TrailingDoubleStar_NeedsAtLeastOneSegment**: `docs/**` does not select the path `docs`, but
selects `docs/a`. This scenario is tested by `SelectionMatcher_IsMatch_TrailingDoubleStar_NeedsAtLeastOneSegment`.

**SelectionMatcher_IsMatch_Star_DoesNotCrossSeparators**: `*` matches within one segment only. This scenario is tested by
`SelectionMatcher_IsMatch_Star_DoesNotCrossSeparators`.

**SelectionMatcher_IsMatch_ManyDoubleStars_FinishesQuickly**: A pattern with many `**` segments against a long path that
does not match returns within the time limit of the test. This scenario is tested by
`SelectionMatcher_IsMatch_ManyDoubleStars_FinishesQuickly`.

**SelectionMatcher_Parse_Lines_AreTrimmedAndNormalized**: Lines are trimmed, blank lines skipped, a leading `./` or `/`
dropped, and a trailing `/` turned into a folder prefix; writing the patterns back with `Normalize` gives one line per
pattern with `!` before an exclusion, and parsing that text again gives the same text. This scenario is tested by
`SelectionMatcher_Parse_Lines_AreTrimmedAndNormalized`.

**SelectionMatcher_Parse_InvalidText_ThrowsSelectionInvalid**: Invalid selections are refused with `SelectionInvalid`. This
scenario is tested by `SelectionMatcher_Parse_InvalidText_ThrowsSelectionInvalid`.

**SelectionMatcher_Parse_Limits_AreInclusive**: Exactly 100 patterns and exactly 400 characters in a line are accepted,
and
one more is refused. This scenario is tested by `SelectionMatcher_Parse_Limits_AreInclusive`.

**SelectionMatcher_NullArguments_ThrowArgumentNull**: Null arguments raise `ArgumentNullException`. This scenario is
tested by `SelectionMatcher_NullArguments_ThrowArgumentNull`.

**SelectionMatcher_IsMatch_CommonSyntax_AgreesWithFileSystemGlobbing**: Include patterns followed by exclusions select the
same paths as the globbing library for the syntax both support (literals, `*`, and `**`). This scenario is tested by
`SelectionMatcher_IsMatch_CommonSyntax_AgreesWithFileSystemGlobbing`.
