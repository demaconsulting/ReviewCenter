### ReviewSetParser

#### Purpose

`ReviewSetParser` (the class `ReviewMarkConfigParser`) reads the review-sets that a repository defines in its ReviewMark
`.reviewmark.yaml` file. Its single responsibility is to turn the bytes of that file into a bounded list of review-set
definitions, or into one plain error, without trusting the file.

#### Data Model

The class is internal, stateless, and thread-safe; it implements the Core interface `IReviewSetParser`. It has one
reused `IDeserializer` of YamlDotNet, built with the camel-case naming convention and with unmatched properties ignored.

**Result**: a list of `ReviewSetDefinition` (defined in `Core`) values, each with `Id`, `Title`, and `Paths` (an ordered
list of include and exclude patterns), in the order of the file.

**Shapes read**: two private shapes, a document with a `reviews` list and an entry with `id`, `title`, and `paths`. Every
other key, at any level, is ignored.

**Limits** (`ReviewLimits` and `SelectionMatcher`): the file is at most 256 KiB, a file has at most 500 sets, an
identifier has at most 100 characters, a title at most 200, a set at most `SelectionMatcher.MaxPatterns` paths, and a
path at most `SelectionMatcher.MaxPatternLength` characters.

#### Key Methods

**Parse**: Reads the review-sets from the bytes of a configuration file.

- *Parameters*: `byte[] content`.
- *Returns*: `IReadOnlyList<ReviewSetDefinition>` — at least one definition, in file order.
- *Preconditions*: `content` is not null (otherwise `ArgumentNullException`).
- *Postconditions*: Every definition has a trimmed, non-empty identifier that is unique in the list, a trimmed non-empty
  title, and paths that are trimmed, not empty, free of control characters, and within the limits.
- *Algorithm*: Refuse a file over the size limit. Decode it as UTF-8, dropping a byte order mark. Read the YAML events
  with the YamlDotNet parser, without building anything, and refuse the file when an alias is found. Deserialize the
  text into the plain shapes. Refuse a file with no `reviews` list or with an empty one, or with too many entries. For
  each entry, in order, trim and check the identifier, the title, and the paths, and refuse a repeated identifier.

#### Error Handling

Every problem is a `DomainRuleException` with `DomainRule.ReviewSetConfigInvalid` and a plain message that names the
file, and the position of the entry (starting at 1) or the line and the column of a YAML error. The message and the
stack of the YAML library never reach the caller. A null argument is `ArgumentNullException`.

#### Dependencies

- **YamlDotNet** — the YAML reader (an OTS item, see the OTS design of YamlDotNet).
- **ReviewSetDefinition**, **IReviewSetParser**, **ReviewLimits**, **SelectionMatcher**, **DomainRuleException**, and
  **DomainRule** — Core.

#### Callers

- **ReviewPlanner** — gives it the bytes of the configuration file of the head commit.
- **ServerHost** — registers it as the Core interface `IReviewSetParser`.
