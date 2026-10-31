### InboxStore

![Storage Structure](StorageView.svg)

#### Purpose

`InboxStore` implements the Core interface `IInboxStore` on top of `ReviewCenterDbContext`. Its responsibilities are to
find the reviews in which a user takes part without loading their files, and to persist what each user has seen of
each review (the read state of the inbox and of the unread markers). It holds no rule of the inbox; the rules are in
`InboxService`. There is no notification table: the inbox is computed from the review data, and the one row per user
and review that this unit stores is all that is kept about the user's reading. The writes are not audited and are not
part of the record digest, because what a person has seen is a personal convenience and not a change of a review.

#### Data Model

The unit is stateless apart from the injected context factory. Every operation creates its own short-lived context from
the factory and disposes it before returning, so the store is safe for concurrent use. The table is `ReviewUserStates`
(see `ReviewCenterDbContext`): one row per user, project, and review holding the highest comment identity the user has
seen and the time the user last saw the review. Both values only ever grow.

#### Key Methods

**ListInvolvedReviewsAsync**: Finds the reviews of a user.

- *Parameters*: The identity and the user name of the user, and a cancellation token.
- *Returns*: One light `InboxCandidate` (project, review identifier, title, creator, due date) for each review in
  which the user is the primary or the alternate of a reviewer slot or is the creator, ordered by project and review
  identifier; empty when there are none.
- *Postconditions*: One projection query runs; no file, thread, or checklist is loaded. The creator column has the
  case-insensitive collation, so the name matches without regard to case and the index on the column serves the
  lookup, as the indexes on the primary and the alternate user of the slots serve the slot lookup.

**ListStatesAsync**: Reads all rows of one user, untracked. The rows of another user are never returned.

**GetStateAsync**: Reads the row of a user for one review, or null.

**MarkSeenAsync**: Records that a user has seen a review without ever moving the state back.

- *Parameters*: The identity of the user, the project and the review, the highest comment identity seen, the time to
  remember, and a cancellation token.
- *Postconditions*: A missing row is inserted. An existing row keeps the higher comment identity and the later time.
  The change is a compare-and-set: one conditional `ExecuteUpdate` matches the values that were read, so a writer that
  changed the row in between wins and the loser reads again, up to a fixed number of rounds. A duplicate key of a racing
  insert makes the loser retry as an update. Parallel calls therefore never fail and the highest values are left. A
  call that would change nothing writes nothing.
- *Errors*: An empty review identifier raises `ArgumentException`. After the last round `InvalidOperationException`
  is raised.

**MarkSeenManyAsync**: Marks several reviews as `MarkSeenAsync` does, with the same time for every review.

#### Error Handling

Any database failure propagates unchanged to the caller. Null or empty arguments raise `ArgumentNullException` or
`ArgumentException`. Deleting a review or a user deletes the rows of the table through the cascade of the schema, so the
unit has no cleanup code.

#### Dependencies

- **ReviewCenterDbContext** — the schema and the context factory.
- **Core domain types and IInboxStore** — the `ReviewUserState`, `InboxCandidate`, and `InboxSeenMark` types and the
  interface implemented.
- **Entity Framework Core** — the projection query and the conditional update.

#### Callers

- **InboxService** — through the Core interface; it is the only unit that uses the store.
- **ServerHost** — registers the store as the implementation of the Core interface.
