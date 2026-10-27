### PersonalAccessTokenService

#### Verification Approach

`PersonalAccessTokenService` is verified with unit tests defined in `PersonalAccessTokenServiceTests.cs`. The tests use
the real service with in-memory fakes of the token store (`FakePersonalAccessTokenStore`, which also keeps the audit
drafts that the service builds) and of the user store, a real `UserService` with a fast fake password hasher for the
owners, and a clock that the tests move. The
fake store can be made to return a row with another hash, so that the second check of the hash is tested. No database,
web framework, or network is involved. The rules are verified here; the storage is verified in the `Storage` subsystem
and the request handling in the `Api` subsystem.

#### Test Environment

N/A - standard test environment (no external resource is needed).

#### Acceptance Criteria

- All tests pass with zero failures.
- A new token has the prefix and 256 bits of random value, and only the SHA-256 hash is stored and no listing has the
  value.
- The display id is short, random, and not taken from the value.
- The name is trimmed and limited, the expiry is one of the three choices, and an unknown or disabled owner is refused.
- No more than 20 tokens are active, where revoked and expired tokens do not count.
- Only the right value authenticates; a malformed value, a revoked or expired token, a disabled or locked owner, and a
  row with another hash are refused.
- The last use is written at most once in five minutes, also for a burst of parallel requests.
- A user revokes only their own tokens, and an actor that came through a token can neither create nor revoke.
- The creation and the revocation are audited with the display id and without the value.

#### Test Scenarios

**PersonalAccessTokenService_Create_NewToken_HasPrefixAndHighEntropyValue**: Two tokens are created. Each value starts
with `rcpat_`, has 43 base64url characters that decode to 32 bytes, and the two values differ. This scenario is tested by
`PersonalAccessTokenService_Create_NewToken_HasPrefixAndHighEntropyValue`.

**PersonalAccessTokenService_Create_NewToken_StoresOnlyTheSha256Hash**: A token is created. The store holds the lowercase
hexadecimal SHA-256 hash of the value and not the value. This scenario is tested by
`PersonalAccessTokenService_Create_NewToken_StoresOnlyTheSha256Hash`.

**PersonalAccessTokenService_Create_DisplayId_IsShortRandomAndIndependentOfTheSecret**: Several tokens are created. The
display id is a short text of the allowed alphabet that differs between tokens and is not part of the value. This
scenario is tested by `PersonalAccessTokenService_Create_DisplayId_IsShortRandomAndIndependentOfTheSecret`.

**PersonalAccessTokenService_List_AfterCreate_NeverReturnsTheValue**: Tokens are listed after they were created. No field
of the listed tokens holds the value. This scenario is tested by
`PersonalAccessTokenService_List_AfterCreate_NeverReturnsTheValue`.

**PersonalAccessTokenService_Create_Name_IsValidatedAndTrimmed**: Empty, blank, and control-character names are refused
with `TokenNameInvalid`, and a valid name is trimmed. This scenario is tested by
`PersonalAccessTokenService_Create_Name_IsValidatedAndTrimmed`.

**PersonalAccessTokenService_Create_NameLength_HasInclusiveLimit**: A name over the limit is refused and one at the limit
is accepted. This scenario is tested by `PersonalAccessTokenService_Create_NameLength_HasInclusiveLimit`.

**PersonalAccessTokenService_CreateAndList_UnknownOrDisabledOwner_IsRefused**: An unknown and a disabled owner cannot
create or list tokens. This scenario is tested by
`PersonalAccessTokenService_CreateAndList_UnknownOrDisabledOwner_IsRefused`.

**PersonalAccessTokenService_Create_ExpiryChoices_SetsExpiryOrRejectsOthers**: Lifetimes of 30, 90, and 365 days set the
expiry that many days ahead, and any other number is refused with `TokenExpiryInvalid`. This scenario is tested by
`PersonalAccessTokenService_Create_ExpiryChoices_SetsExpiryOrRejectsOthers`.

**PersonalAccessTokenService_Authenticate_AfterExpiry_IsRejected**: A token is accepted before its expiry and refused when
the clock reaches it. This scenario is tested by `PersonalAccessTokenService_Authenticate_AfterExpiry_IsRejected`.

**PersonalAccessTokenService_Create_TwentyActive_RejectsTheTwentyFirst**: Twenty tokens are created and the twenty-first
is refused with `TokenLimitReached`. This scenario is tested by
`PersonalAccessTokenService_Create_TwentyActive_RejectsTheTwentyFirst`.

**PersonalAccessTokenService_Create_RevokedAndExpiredDoNotCount**: With twenty tokens of which some are revoked or
expired, a new token can be created. This scenario is tested by
`PersonalAccessTokenService_Create_RevokedAndExpiredDoNotCount`.

**PersonalAccessTokenService_Authenticate_RightAndWrongValue_OnlyRightSucceeds**: The right value authenticates as the
owner; a wrong value does not. This scenario is tested by
`PersonalAccessTokenService_Authenticate_RightAndWrongValue_OnlyRightSucceeds`.

**PersonalAccessTokenService_Authenticate_StoreReturnsRowWithOtherHash_IsRejected**: The fake store returns a row whose
hash differs from the hash of the presented value. The service refuses it, because it compares the hash again in constant
time. This scenario is tested by
`PersonalAccessTokenService_Authenticate_StoreReturnsRowWithOtherHash_IsRejected`.

**PersonalAccessTokenService_Authenticate_DisabledOrLockedOwner_IsRejected**: The token of a disabled owner and of a
locked-out owner is refused, and works again when the owner does. This scenario is tested by
`PersonalAccessTokenService_Authenticate_DisabledOrLockedOwner_IsRejected`.

**PersonalAccessTokenService_Authenticate_Malformed_IsRejected**: Values of the wrong shape are refused without asking the
store. This scenario is tested by `PersonalAccessTokenService_Authenticate_Malformed_IsRejected`.

**PersonalAccessTokenService_Authenticate_Touch_AtMostOncePerFiveMinutes**: A token is used several times within five
minutes and then after five minutes. The last-used time is written once for the first use and again only after the five
minutes. This scenario is tested by `PersonalAccessTokenService_Authenticate_Touch_AtMostOncePerFiveMinutes`.

**PersonalAccessTokenService_Authenticate_ParallelBurst_TouchesOnce**: Four requests run in parallel and all read the same
snapshot of a token that was never used. All are accepted and the store is written once, because the store applies the
update only to the last-used time that was read. This scenario is tested by
`PersonalAccessTokenService_Authenticate_ParallelBurst_TouchesOnce`.

**PersonalAccessTokenService_Revoke_OwnToken_StopsWorkingAndIsAudited**: A user revokes their own token. It stops working
at once and the revocation is recorded. This scenario is tested by
`PersonalAccessTokenService_Revoke_OwnToken_StopsWorkingAndIsAudited`.

**PersonalAccessTokenService_Revoke_OtherUsersToken_IsRefused**: A user revokes the token of somebody else, a token that
does not exist, and a token that is already revoked. Each is refused in the same way and the token of the other user
still works. This scenario is tested by `PersonalAccessTokenService_Revoke_OtherUsersToken_IsRefused`.

**PersonalAccessTokenService_CreateAndRevoke_ApiTokenActor_IsRefused**: An actor that carries the id of a token tries to
create and to revoke a token. Both are refused with `ActorNotAllowed`. This scenario is tested by
`PersonalAccessTokenService_CreateAndRevoke_ApiTokenActor_IsRefused`.

**PersonalAccessTokenService_CreateAndRevoke_RecordAuditEventsWithoutTheValue**: A token is created and revoked. The audit
trail has `token.created` and `token.revoked` with the display id, and no entry has the value or its hash. This scenario
is tested by `PersonalAccessTokenService_CreateAndRevoke_RecordAuditEventsWithoutTheValue`.
