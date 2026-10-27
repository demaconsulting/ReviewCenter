### PersonalAccessTokenStore

#### Verification Approach

`PersonalAccessTokenStore` is verified with unit tests defined in `PersonalAccessTokenStoreTests.cs`. The tests use the
real store over a real SQLite database file in a temporary folder (`TestStore`), add the owners to the users table
directly, and read the rows and the audit entries back with a second context. The test of the `ReviewCenterDbContext`
model for the token table and the creator columns of a review is in the same file and is described in the verification of
that unit.

#### Test Environment

N/A - standard test environment (a temporary SQLite database file is created by each test and removed afterwards).

#### Acceptance Criteria

- All tests pass with zero failures.
- A token round-trips with every field, gets a database-assigned identity, and is saved with its audit entry.
- A hash that is already stored is refused and nothing is audited.
- A token is found by its hash with its owner, and only the tokens of the user are listed, newest first.
- A revocation sets the time once and audits it; a second revocation, a token of another user, and a missing token change
  nothing and write no entry.
- A use changes only the last-used time, writes no audit entry, and does not undo a revocation.
- Of several parallel uses that read the same last-used time exactly one writes.

#### Test Scenarios

**PersonalAccessTokenStore_AddAsync_Token_RoundTripsAndAudits**: A token is added with a supplied identity that must be
ignored. The stored token has a database-assigned identity and every other field, only the hash is in the row, and the
audit entry that the function built is saved with it. This scenario is tested by
`PersonalAccessTokenStore_AddAsync_Token_RoundTripsAndAudits`.

**PersonalAccessTokenStore_AddAsync_DuplicateHash_IsRefusedAndNothingIsAudited**: A second token with the same hash is
added. The database refuses it and no second audit entry exists. This scenario is tested by
`PersonalAccessTokenStore_AddAsync_DuplicateHash_IsRefusedAndNothingIsAudited`.

**PersonalAccessTokenStore_FindByHashAsync_Hash_FindsTokenWithOwner**: Tokens are found by hash. The token with the hash
is returned with its owner loaded, and another hash finds nothing. This scenario is tested by
`PersonalAccessTokenStore_FindByHashAsync_Hash_FindsTokenWithOwner`.

**PersonalAccessTokenStore_ListForUserAsync_Tokens_AreOwnedAndNewestFirst**: Tokens of two users are stored, one of them
revoked. The list of a user holds only their tokens, revoked ones included, newest first. This scenario is tested by
`PersonalAccessTokenStore_ListForUserAsync_Tokens_AreOwnedAndNewestFirst`.

**PersonalAccessTokenStore_RevokeAsync_OwnToken_RevokesOnceAndAudits**: An own token is revoked. The time is set and the
entry is written; a second revocation, the token of another user, and a missing token return nothing and write no entry.
This scenario is tested by `PersonalAccessTokenStore_RevokeAsync_OwnToken_RevokesOnceAndAudits`.

**PersonalAccessTokenStore_TouchAsync_Token_ChangesOnlyLastUsed**: A use of a token and a use of a revoked token are
recorded. Only the last-used time changes, no audit entry is written, and the revocation stays. This scenario is tested
by `PersonalAccessTokenStore_TouchAsync_Token_ChangesOnlyLastUsed`.

**PersonalAccessTokenStore_TouchAsync_ParallelSameExpected_WritesOnce**: Eight uses run in parallel against a SQLite
database, all having read the same last-used time (none for a token never used, or an earlier time). Exactly one returns
true and its time is stored; a later use that still holds the old time returns false and changes nothing. This scenario
is tested by `PersonalAccessTokenStore_TouchAsync_ParallelSameExpected_WritesOnce`.
