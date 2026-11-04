### ReviewSetParser

#### Verification Approach

`ReviewSetParser` is verified with unit tests defined in `ReviewMarkConfigParserTests.cs`. The parser is a pure function
of bytes, so the tests give it the bytes of files and check the definitions or the exception: the real
`.reviewmark.yaml` of this repository (found by looking for the solution file above the test folder), files written in
the test to cover the styles of YAML, files with every kind of error, and files built exactly at and one past every
limit. No git and no database are needed; the reading of a configuration file through a real git repository is verified
by the `ContentSources` subsystem test and by the planner tests that run against real git.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All tests pass with zero failures.
- The real configuration file of this repository gives every review-set, in file order, with unique identifiers and
  paths that the selection matcher accepts.
- Keys that are not `reviews`, `id`, `title`, or `paths` are ignored at every level.
- Block, flow, quoted, and folded YAML, a byte order mark, and Windows line endings are read as YAML defines them, and
  values are trimmed.
- Every broken file (including an identifier or a title with a control character) is refused with
  `ReviewSetConfigInvalid` and a plain message that does not show the YAML library.
- A file exactly at each limit is accepted, one past it is refused, a file with an alias is refused, and a file over the
  size limit is refused before it is read as YAML.

#### Test Scenarios

**ReviewMarkConfigParser_Parse_RepositoryOwnConfiguration_ReadsEverySet**: The `.reviewmark.yaml` of this repository is
read; there are more than ten sets, the identifiers are unique, every set has a selection that the selection matcher
accepts, and the first set has the identifier, the title, and the paths written in the file. This scenario is tested by
`ReviewMarkConfigParser_Parse_RepositoryOwnConfiguration_ReadsEverySet`.

**ReviewMarkConfigParser_Parse_UnknownKeys_AreIgnored**: A file with other top-level keys and extra keys inside an entry
gives the two sets in file order with their ordered paths. This scenario is tested by
`ReviewMarkConfigParser_Parse_UnknownKeys_AreIgnored`.

**ReviewMarkConfigParser_Parse_YamlStyles_AreUnderstood**: A file with a byte order mark, Windows line endings, flow
style, quoting, a folded block scalar, and padded values gives the values as YAML defines them, trimmed. This scenario is
tested by `ReviewMarkConfigParser_Parse_YamlStyles_AreUnderstood`.

**ReviewMarkConfigParser_Parse_BlockStyle_IsRead**: A file in block style with two sets gives both sets with their
titles and paths. This scenario is tested by `ReviewMarkConfigParser_Parse_BlockStyle_IsRead`.

**ReviewMarkConfigParser_Parse_FlowStyle_IsRead**: A file whose entry and path list are written in flow style (braces
and brackets) gives the set with its title and both paths. This scenario is tested by
`ReviewMarkConfigParser_Parse_FlowStyle_IsRead`.

**ReviewMarkConfigParser_Parse_QuotedStrings_AreRead**: A file with single-quoted and double-quoted values, including a
colon and an escaped quote in a title, gives the values without the quotes and with the escapes applied. This scenario is
tested by `ReviewMarkConfigParser_Parse_QuotedStrings_AreRead`.

**ReviewMarkConfigParser_Parse_FoldedText_IsRead**: A title written as a folded block scalar over two lines is read as
one line, with the lines joined by one space. This scenario is tested by
`ReviewMarkConfigParser_Parse_FoldedText_IsRead`.

**ReviewMarkConfigParser_Parse_PaddedValues_AreTrimmed**: An identifier, a title, and two paths that are quoted with
spaces around them are read without the spaces. This scenario is tested by
`ReviewMarkConfigParser_Parse_PaddedValues_AreTrimmed`.

**ReviewMarkConfigParser_Parse_SetWithoutPaths_HasEmptyPathList**: A review-set without a `paths` key and one with an
empty `paths` list are both read, and both have no paths. This scenario is tested by
`ReviewMarkConfigParser_Parse_SetWithoutPaths_HasEmptyPathList`.

**ReviewMarkConfigParser_Parse_ByteOrderMark_IsSkipped**: A small file whose bytes start with a UTF-8 byte order mark is
read, and its one review-set has the expected identifier and title. This scenario is tested by
`ReviewMarkConfigParser_Parse_ByteOrderMark_IsSkipped`.

**ReviewMarkConfigParser_Parse_WindowsLineEnds_AreUnderstood**: A small file with Windows line endings is read, and its
review-set and path have the expected values. This scenario is tested by
`ReviewMarkConfigParser_Parse_WindowsLineEnds_AreUnderstood`.

**ReviewMarkConfigParser_Parse_BrokenFile_ThrowsConfigInvalid**: A theory over files that are empty, have no `reviews`
list, an empty list, an entry without an identifier or a title, a repeated identifier, broken YAML, entries of the wrong
shape, and empty or control-character paths, identifiers, or titles; each gives the rule `ReviewSetConfigInvalid` and a
message with the expected
part that shows no library text. This scenario is tested by `ReviewMarkConfigParser_Parse_BrokenFile_ThrowsConfigInvalid`.

**ReviewMarkConfigParser_Parse_Limits_AreEnforced**: Files exactly at the identifier, title, path count, path length, and
set count limits are read; files one past each limit are refused with the rule. This scenario is tested by
`ReviewMarkConfigParser_Parse_Limits_AreEnforced`.

**ReviewMarkConfigParser_Parse_SetCountAtLimit_IsRead**: A file with exactly 500 review-sets is read and gives all of
them. This scenario is tested by `ReviewMarkConfigParser_Parse_SetCountAtLimit_IsRead`.

**ReviewMarkConfigParser_Parse_IdLengthAtLimit_IsRead**: An identifier of exactly 100 characters is read in full. This
scenario is tested by `ReviewMarkConfigParser_Parse_IdLengthAtLimit_IsRead`.

**ReviewMarkConfigParser_Parse_TitleLengthAtLimit_IsRead**: A title of exactly 200 characters is read in full. This
scenario is tested by `ReviewMarkConfigParser_Parse_TitleLengthAtLimit_IsRead`.

**ReviewMarkConfigParser_Parse_PathCountAtLimit_IsRead**: A review-set with exactly 100 paths is read and gives all of
them. This scenario is tested by `ReviewMarkConfigParser_Parse_PathCountAtLimit_IsRead`.

**ReviewMarkConfigParser_Parse_PathLengthAtLimit_IsRead**: A path of exactly 400 characters is read in full. This
scenario is tested by `ReviewMarkConfigParser_Parse_PathLengthAtLimit_IsRead`.

**ReviewMarkConfigParser_Parse_TooManySets_ThrowsConfigInvalid**: A file with 501 review-sets is refused with the rule
`ReviewSetConfigInvalid` and a message that names the limit of 500 review-sets. This scenario is tested by
`ReviewMarkConfigParser_Parse_TooManySets_ThrowsConfigInvalid`.

**ReviewMarkConfigParser_Parse_IdTooLong_ThrowsConfigInvalid**: An identifier of 101 characters is refused with the rule
and a message that names the identifier limit. This scenario is tested by
`ReviewMarkConfigParser_Parse_IdTooLong_ThrowsConfigInvalid`.

**ReviewMarkConfigParser_Parse_TitleTooLong_ThrowsConfigInvalid**: A title of 201 characters is refused with the rule
and a message that names the title limit. This scenario is tested by
`ReviewMarkConfigParser_Parse_TitleTooLong_ThrowsConfigInvalid`.

**ReviewMarkConfigParser_Parse_TooManyPaths_ThrowsConfigInvalid**: A review-set with 101 paths is refused with the rule
and a message that names the limit of 100 paths. This scenario is tested by
`ReviewMarkConfigParser_Parse_TooManyPaths_ThrowsConfigInvalid`.

**ReviewMarkConfigParser_Parse_PathTooLong_ThrowsConfigInvalid**: A path of 401 characters is refused with the rule and
a message that names the review-set that holds it. This scenario is tested by
`ReviewMarkConfigParser_Parse_PathTooLong_ThrowsConfigInvalid`.

**ReviewMarkConfigParser_Parse_EmptyPath_ThrowsConfigInvalid**: A review-set with an empty path next to a valid one is
refused with the rule and a message that names the review-set that holds the path. This scenario is tested by
`ReviewMarkConfigParser_Parse_EmptyPath_ThrowsConfigInvalid`.

**ReviewMarkConfigParser_Parse_PathWithControlCharacter_ThrowsConfigInvalid**: A path with a line feed in the middle is
refused with the rule and a message that names the review-set that holds the path. This scenario is tested by
`ReviewMarkConfigParser_Parse_PathWithControlCharacter_ThrowsConfigInvalid`.

**ReviewMarkConfigParser_Parse_FileAtSizeLimit_IsRead**: A valid file that is exactly 256 KiB is read and gives its
review-set. This scenario is tested by `ReviewMarkConfigParser_Parse_FileAtSizeLimit_IsRead`.

**ReviewMarkConfigParser_Parse_FileTooLarge_ThrowsConfigInvalid**: A file one byte over 256 KiB is refused with the rule,
and the message gives the limit. This scenario is tested by `ReviewMarkConfigParser_Parse_FileTooLarge_ThrowsConfigInvalid`.

**ReviewMarkConfigParser_Parse_Alias_ThrowsConfigInvalid**: A file that repeats a list through an alias is refused with
the rule and a message that names the alias. This scenario is tested by
`ReviewMarkConfigParser_Parse_Alias_ThrowsConfigInvalid`.

**ReviewMarkConfigParser_Parse_Null_Throws**: Null content is refused with `ArgumentNullException`. This scenario is
tested by `ReviewMarkConfigParser_Parse_Null_Throws`.
