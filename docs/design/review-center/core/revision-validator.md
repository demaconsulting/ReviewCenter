### RevisionValidator

![Core Structure](CoreView.svg)

#### Purpose

`RevisionValidator` decides whether text is acceptable as the name of a branch, tag, or commit. Its single responsibility
is to refuse everything that git could read as an option, a range, or a revision expression, before the text is passed
to git. It is a pure, static unit.

#### Data Model

**MaxLength**: `int` — 200, the longest accepted revision.

The accepted alphabet: the first character is an ASCII letter or digit; the others are ASCII letters or digits, or one of
`.`, `_`, `/`, `+`, `@`, `#`, `-`.

#### Key Methods

**Validate**: Checks a revision and returns it.

- *Parameters*: `string? revision` — white space is not trimmed.
- *Returns*: `string` — the same revision, unchanged.
- *Preconditions*: None; a null revision is invalid.
- *Postconditions*: The result is non-empty, at most `MaxLength` characters, and passes every rule below.
- *Algorithm*: Reject a null or empty revision and one longer than `MaxLength`. Reject a first character that is not
  an ASCII
  letter or digit (so a revision can never start with `-`) and any character outside the alphabet. This already excludes
  spaces, control characters, `~`, `^`, `:`, `?`, `*`, `[`, and `\`, so no revision expression, range, or reference log
  syntax gets through. Also reject the sequences `..`, `@{`, and `//`, a trailing `/` or `.`, and a name that ends with
  `.lock`.

**IsValid**: The same check as a `bool`, without an exception.

#### Error Handling

An invalid revision, including a null or empty one, raises `DomainRuleException` for `RevisionInvalid` with one message
that states the rule: 1 to 200 characters, a branch, tag, or commit name made of letters, digits, and `. _ / + @ # -`,
starting with a letter or digit. Whether the revision exists is not decided here; `GitSource` reports `RevisionNotFound`.

#### Dependencies

- **DomainRuleException**, **DomainRule** — Core.

#### Callers

- **GitSource** — validates a revision before it asks git to resolve it.
- **Review creation from a repository** (a later slice) — validates the revisions entered by the user.
