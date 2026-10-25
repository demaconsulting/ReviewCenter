### AuditChain

![Core Structure](CoreView.svg)

#### Purpose

`AuditChain` is the unit that makes the audit trail tamper-evident. Its single responsibility is the hash chain: it
defines what one audit entry is, computes the hash of an entry from a canonical encoding, and verifies a whole chain,
naming the first entry that does not fit. It owns the data types of the trail (`AuditEntry`, `AuditDraft`,
`AuditVerificationResult`) and the text helper `AuditText`, which makes text safe to hash and to store. It is
deliberately free of storage and web technology, so that the rules can be reviewed and tested on their own.

#### What The Chain Proves And What It Does Not

Every entry carries the hash of the entry before it, and its own hash covers all of its fields including that
previous hash. This gives the following, and nothing more:

- **Detected**: a changed entry (the stored hash no longer matches the fields), an entry removed from the middle of
  the history (a gap in the sequence, and the next entry no longer names the hash of its predecessor), an entry
  inserted into the middle, a repeated entry, and entries put in another order. Verification names the first bad entry.
- **Not detected**: removal of the newest entries (what is left is a shorter but still valid chain), and a complete
  rewrite of the whole chain by someone who can write to the database, because that person can recompute every hash
  with the same public algorithm. Both stay detectable only when the head hash (the hash of the newest entry), or the
  entry count, was recorded somewhere the database writer cannot reach, so that the recorded value can be compared
  with the chain later. The head hash is shown by the `Audit` page and by the result of `audit verify` for exactly
  this purpose. This slice has no external anchoring: nothing records the head hash outside the database, so recording
  it is an operator procedure (see the user guide).
- **Not covered**: whether the recorded events are true. The chain protects entries after they are written; it
  says nothing about an action that was never recorded, because the recording failed (see `AuditService`) or
  because the change was made directly in the database.

#### Data Model

`AuditChain`, `AuditText`, and the verification result are stateless, so they are thread-safe. `AuditChainVerifier`
keeps state and is not thread-safe; one instance verifies one chain.

**AuditEntry**: one entry of the trail; immutable once created.

| Field | Type | Meaning |
| ----- | ---- | ------- |
| `Sequence` | `long` | Position in the chain; the first entry is 1; there are no gaps |
| `TimestampUtc` | `DateTimeOffset` | When the entry was recorded, in UTC |
| `ActorUserId` | `int?` | The acting user when the actor is a known user; otherwise `null` |
| `ActorName` | `string` | The actor as text at that time, `Display Name (username)` or plain text |
| `Action` | `string` | The action code, for example `review.created` |
| `ProjectId` | `int?` | The project the action belongs to, or `null` |
| `ReviewId` | `string?` | The review the action belongs to, or `null` |
| `Detail` | `string` | One-line description; never comment text, passwords, hashes of passwords, or tokens |
| `PreviousHash` | `string` | The hash of the previous entry; the genesis hash for the first entry |
| `Hash` | `string` | The hash of this entry as 64 lowercase hexadecimal characters |

The actor is stored as a snapshot of text and not only as a reference, so that a later change of a display name, or the
removal of a user, cannot rewrite history. There are no foreign keys from the trail to other data.

**AuditDraft**: what a service wants recorded, without the parts the store assigns (sequence, time, hashes). It
holds `Action`, `ActorUsername` (`null` when the actor is not a known user), `ActorDisplayName`, `ProjectId`,
`ReviewId`, and `Detail`. Its `ActorName` property composes the stored actor text: `Display Name (username)` for a
signed-in user, and the plain text for the command line (`command line`) and for an unknown actor (`unknown user`).
`AuditDraft.By(actor, action, detail, projectId, reviewId)` builds a draft for a signed-in user or for
`Actor.CommandLine`.

**GenesisHash**: `string` (constant), 64 times the character `0`; the `PreviousHash` of the first entry.

**AuditVerificationResult**: `IsIntact`, `Count` (entries that verified; for a broken chain, the entries before the
break), `FirstBadSequence` (`null` when intact), and `HeadHash` (the hash of the last entry that verified; the genesis
hash for an empty chain).

**AuditText limits**: `Action` at most 100 characters, `ActorName` at most 200, and `Detail` at most 500.

#### Canonical Encoding

The hash is SHA-256 over a canonical byte encoding of the entry, written as 64 lowercase hexadecimal characters. The
encoding (version 1) writes these fields in this fixed order:

| No. | Field | Encoding |
| --- | ----- | -------- |
| 1 | Domain separator | string `ReviewCenter.Audit.v1` |
| 2 | `Sequence` | 8 bytes, signed, big-endian |
| 3 | `TimestampUtc` | its UTC ticks, 8 bytes, signed, big-endian |
| 4 | `ActorUserId` | presence byte (0 or 1), then 8 bytes big-endian when present |
| 5 | `ActorName` | string |
| 6 | `Action` | string |
| 7 | `ProjectId` | presence byte, then 8 bytes big-endian when present |
| 8 | `ReviewId` | presence byte, then string when present |
| 9 | `Detail` | string |
| 10 | `PreviousHash` | string |

A **string** is the UTF-8 bytes of the text, preceded by their count as 4 bytes, big-endian. A
text that is not valid Unicode is encoded with replacement characters rather than refused, because a hash must always
be computable.

The encoding is unambiguous: every field is fixed-width or length-prefixed, and an optional value starts with a
presence byte, so `null` and an empty text differ and moving text from one field into the next one changes the hash
("ab" and "c" never hash like "a" and "bc"). The domain separator names the version, so a future change of the
encoding can never be mistaken for this one. The time is hashed as UTC ticks, so the same instant gives the same hash
whatever offset the value carries. `ComputeHash` uses the fields of the entry only and ignores its stored `Hash`.

Two known-answer hashes are pinned in the tests (one entry with every field, one with the optional fields absent), so a
change of the encoding cannot pass unnoticed: it would make every stored chain fail verification.

#### Verification Algorithm

Verification is a single ordered pass; it holds one entry at a time and so needs constant memory.

`AuditChainVerifier` keeps the number of the next expected entry (starting at 1), the hash of the last good entry
(starting at the genesis hash), and the count of good entries. For each entry in sequence order `Add` applies three
checks and the first failure wins:

1. The entry's `Sequence` must be the expected number. If it is not (an entry is missing, repeated, or out of order),
   the first bad sequence is the *expected* number: the place where the chain breaks.
2. Its `PreviousHash` must equal the hash of the last good entry.
3. Its stored `Hash` must equal the hash recomputed from its fields.

When the second or third check fails, the first bad sequence is the entry's own sequence. After the first failure
the verifier remembers it and refuses all later entries, so the first bad sequence is stable. A chain whose first
entry has another number than 1 breaks at 1; an empty chain is intact with the genesis hash as its head.

An edit of an entry that also recomputes that entry's hash is not hidden: the next entry still names the old hash,
so the chain breaks at the next entry. `AuditChain.Verify` runs the verifier over a sequence of entries and stops at
the first failure. `AuditService.VerifyAsync` runs the same verifier over batches read from the store, so the whole
trail is never in memory.

#### Key Methods

**ComputeHash**: Computes the hash of an entry.

- *Parameters*: `AuditEntry entry`.
- *Returns*: The hash as 64 lowercase hexadecimal characters.
- *Preconditions*: `entry` is not null.
- *Postconditions*: The result depends only on the fields named in the encoding.

**Verify**: Verifies a whole chain in order.

- *Parameters*: `IEnumerable<AuditEntry> entries` in sequence order, starting with sequence 1.
- *Returns*: An `AuditVerificationResult`.
- *Postconditions*: Enumeration stops at the first entry that fails.

**AuditChainVerifier.Add**: Verifies the next entry (see the algorithm above); returns `true` while the chain is intact.

**AuditText.Clean**: Makes text safe and bounded: control characters and line or paragraph separators become spaces, a
lone surrogate becomes U+FFFD, leading and trailing white space is removed, and a longer text is cut so that it ends in
`...` and never exceeds the limit, without splitting a surrogate pair. Cleaning happens *before* hashing and storing,
so the stored text is exactly the hashed text and verification never depends on how a database returns text.

**AuditText.ShortHash**: Returns the first 8 lowercase hexadecimal characters of the SHA-256 of the trimmed UTF-8 text.
The trail uses it to identify a comment or a justification without storing it. Eight characters are enough to tell two
texts apart but are a deliberate truncation; a very short or guessable text could be confirmed by someone who guesses
it.

**AuditDraft.By**: Builds a draft for an actor (see the data model).

#### Error Handling

A null argument is rejected with `ArgumentNullException`, and a maximum length below 4 for `Clean` with
`ArgumentOutOfRangeException`. A chain that does not verify is not an error: it is the normal result of
`AuditVerificationResult` with `IsIntact` false.

#### Dependencies

- **Actor** (Core) — the acting user that `AuditDraft.By` turns into a draft.
- **.NET runtime library** — SHA-256, UTF-8 encoding, and big-endian number writing.

#### Callers

- **AuditService** — verifies the trail with the streaming verifier, and passes drafts to the store.
- **AuditAppender** (Storage, in the `AuditStore` unit) — cleans the text, computes the hash of each new entry, and
  uses the genesis hash for the first entry.
- **ProjectService, ReviewService, ThreadService, ChecklistService, SigningService, and UserService** — build drafts
  with `AuditDraft.By` and the text helpers.
- **Tests** — pin the encoding with known-answer hashes and exercise every kind of tampering.
