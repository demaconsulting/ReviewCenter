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

**ReviewMarkConfigParser_Parse_BrokenFile_ThrowsConfigInvalid**: A theory over files that are empty, have no `reviews`
list, an empty list, an entry without an identifier or a title, a repeated identifier, broken YAML, entries of the wrong
shape, and empty or control-character paths, identifiers, or titles; each gives the rule `ReviewSetConfigInvalid` and a
message with the expected
part that shows no library text. This scenario is tested by `ReviewMarkConfigParser_Parse_BrokenFile_ThrowsConfigInvalid`.

**ReviewMarkConfigParser_Parse_Limits_AreEnforced**: Files exactly at the identifier, title, path count, path length, and
set count limits are read; files one past each limit are refused with the rule. This scenario is tested by
`ReviewMarkConfigParser_Parse_Limits_AreEnforced`.

**ReviewMarkConfigParser_Parse_FileTooLarge_ThrowsConfigInvalid**: A file one byte over 256 KiB is refused with the rule,
and the message gives the limit. This scenario is tested by `ReviewMarkConfigParser_Parse_FileTooLarge_ThrowsConfigInvalid`.

**ReviewMarkConfigParser_Parse_Alias_ThrowsConfigInvalid**: A file that repeats a list through an alias is refused with
the rule and a message that names the alias. This scenario is tested by
`ReviewMarkConfigParser_Parse_Alias_ThrowsConfigInvalid`.

**ReviewMarkConfigParser_Parse_Null_Throws**: Null content is refused with `ArgumentNullException`. This scenario is
tested by `ReviewMarkConfigParser_Parse_Null_Throws`.
