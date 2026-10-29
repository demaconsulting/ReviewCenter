### TotpChallengeStore

![Core Structure](CoreView.svg)

#### Purpose

`TotpChallengeStore` holds the short-lived challenges between the two steps of a sign-in with a second factor. After a
right password the sign-in does not issue a session; it issues a challenge, and only a right code for that challenge
completes the sign-in. The store keeps the challenges in memory, hands out an unguessable token that names one, and
limits how often a challenge can be tried. It is a singleton and is thread-safe.

#### Data Model

**TotpChallenge**: an immutable record of `TokenHash`, `Username`, `ClientAddress`, `ReturnUrl`, `Expires`, and
`Failures`. It is the state behind one token: the user who gave the right password, the address the password came from,
the safe local address to go to after the sign-in, the end of its life, and the number of wrong codes so far.

**Token**: 32 random bytes (256 bits) written in the URL-safe form of Base64. Only the SHA-256 value of the token is
kept (in hexadecimal, as the key), so a copy of the memory of the process does not give a usable token. The token is
sent to the browser in the challenge cookie (see `TotpSignIn`).

**Limits**: a challenge lives 5 minutes (`Lifetime`), allows 5 wrong codes (`MaxFailures`), and the store holds at most
10,000 challenges (`MaxEntries`). When the store is full, the ended challenges are dropped first, and when that is not
enough the one that ends first is dropped, so a flood of passwords cannot make the store grow without bound.

**Binding**: a challenge is bound to the user name, to the client address, and to the return address. A missing or
empty address is stored as `unknown`. A token presented from another address finds nothing.

#### Key Methods

**Issue**: Creates a challenge.

- *Parameters*: The user name, the client address, and the return address.
- *Returns*: The token for the cookie.
- *Postconditions*: A new challenge with the full lifetime and no failures is stored under the hash of the token.

**Peek**: Reads a challenge without using it.

- *Parameters*: The token and the client address.
- *Returns*: The challenge, or null for an unknown or ended token or an address that does not match.
- *Postconditions*: Nothing changes. The login page and the first check of the sign-in use it.

**Take**: Removes a challenge and returns it, atomically.

- *Parameters*: The token and the client address.
- *Returns*: The challenge, or null for an unknown token, an ended one (which is removed), or an address that does not
  match (the challenge stays, so another address cannot destroy it).
- *Postconditions*: Of several concurrent requests with the same token exactly one gets the challenge, so one challenge
  cannot be tried in parallel.

**Return**: Gives a challenge back after a wrong code.

- *Parameters*: The challenge that was taken.
- *Returns*: True when it was stored again; false when this was the fifth wrong code or the challenge has ended, so the
  challenge is gone and the person signs in again with the password.
- *Postconditions*: The failure count is one higher in the stored challenge; the end of the life does not move.

**InvalidateUser**: Removes every challenge of a user name, without regard to case. It is called when the second factor
of that user changes (set up, turned off, reset), so an older challenge cannot complete a sign-in under other rules.

**Count** (internal): the number of stored challenges, for tests.

#### Error Handling

Null arguments raise `ArgumentNullException`. A refusal is a null or false result and never an exception. The token is
never logged or put into a message.

#### Dependencies

- **TimeProvider** — supplies the time (injected).
- **.NET runtime library** — random bytes, SHA-256, and a dictionary guarded by one lock.

#### Callers

- **TotpService** — issues the challenge when a sign-in starts, peeks, takes, and returns it, and invalidates the
  challenges of a user.
- **ServerHost** — registers the store as a singleton.
