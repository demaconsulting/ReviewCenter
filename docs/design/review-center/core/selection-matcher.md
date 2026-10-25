### SelectionMatcher

![Core Structure](CoreView.svg)

#### Purpose

`SelectionMatcher` decides which paths of a repository revision belong to a review. Its single responsibility is to parse
the file patterns that a user enters and to test a path against them. It is a pure, static unit with no state, no file
system access, and no dependency on a glob library, so its behavior is fully defined and tested here.

#### Data Model

**SelectionPattern** (supporting type): one parsed pattern.

- **Text**: `string` — the normalized pattern as entered.
- **Exclude**: `bool` — true for a pattern that started with `!`.
- **Segments**: `IReadOnlyList<string>` — the pattern split on `/`.

A `SelectionPattern` is created only by `Parse`, so every instance is valid.

**Limits**: at most 100 patterns, at most 400 characters per line.

#### Key Methods

**Parse**: Turns the text of a selection into patterns.

- *Parameters*: `string text` — one pattern per line.
- *Returns*: `IReadOnlyList<SelectionPattern>` — in the order entered.
- *Preconditions*: `text` is not null.
- *Postconditions*: The list has between one and 100 patterns, each valid.
- *Algorithm*: Split on line breaks; trim each line; skip blank lines. Reject a line longer than 400 characters, a line
  with a control character, a `\`, or an exclusion mark with nothing after it. Remove a leading `!` (marking the pattern
  as an exclusion) and then a leading `./` or `/`. Split on `/`; reject an empty segment, a `.` segment, and a `..`
  segment. A trailing `/` means a directory prefix and appends a `**` segment. Reject a text that yields no pattern
  ("Enter at least one file pattern.") or more than 100.

**IsMatch**: Decides whether a path is selected.

- *Parameters*: `string path` — a repository path with `/` separators; `IReadOnlyList<SelectionPattern> patterns`.
- *Returns*: `bool` — true when the last pattern that matches the path is an include pattern.
- *Preconditions*: Neither argument is null.
- *Algorithm*: Test the patterns in order and remember the result of the last one that matches (include selects, exclude
  deselects); when none matches the path is not selected. A single pattern matches by a recursive comparison of the
  pattern segments with the path segments, memoized on the pair of positions so that many `**` segments cannot cause
  exponential work. A `**` segment matches zero or more path segments, except as the last segment, where it needs at
  least one. Inside a segment `*` matches any run of characters (not crossing `/`), `**` behaves like `*`, and `?`
  matches exactly one character. Comparison is ordinal and case-sensitive; every other character, including `[`, `]`,
  `{`, and `}`, matches itself.

#### Semantics Compared With Other Glob Libraries

The pattern list works like the file lists of ReviewMark, which uses `Microsoft.Extensions.FileSystemGlobbing`. The
differences were chosen deliberately: a repository path is exact and case-sensitive, and the text comes from a web form,
so it must be strict.

| Aspect | `SelectionMatcher` | ReviewMark / FileSystemGlobbing |
| --- | --- | --- |
| Letter case | Case-sensitive, ordinal | Case-insensitive |
| `?` | Matches exactly one character | Not a wildcard (matches itself) |
| `\` | Rejected as invalid | Converted to a path separator |
| `.` and `..` segments | Rejected | `..` allowed at the start |
| Empty segments | Rejected | Tolerated |
| Control characters | Rejected | Not checked |
| Size limits | At most 100 patterns of at most 400 characters | None |
| Input form | One pattern per line; blank lines skipped; no comment syntax | A list of strings from a file |
| No pattern at all | Error, "Enter at least one file pattern." | Treated as all files |
| Only `!` patterns | Selects nothing | Excludes from all files |
| `[...]` and `{...}` | Literal characters | Literal characters (the same) |

The behaviors that are the same: patterns are evaluated in order; `!` excludes; a later include adds a path back; a bare
name is an exact path; a trailing `/` is a directory prefix; a trailing `**` needs at least one more segment; a leading
`./` or `/` is dropped; `*` stays within a segment; `**` as a segment spans directories; and only the path strings are
matched, never the files, so a path that was deleted in the head revision still matches. A differential test compares the
two implementations over a corpus of common syntax (without `?`) and requires them to agree.

#### Error Handling

An invalid selection raises `DomainRuleException` for `SelectionInvalid` with a message that names the cause, for example
that the separator must be `/`. `IsMatch` raises none. Null arguments raise `ArgumentNullException`.

#### Dependencies

- **DomainRuleException**, **DomainRule** — Core.

#### Callers

- **Review creation from a repository** (a later slice) — parses the entered selection and filters the changed files.
