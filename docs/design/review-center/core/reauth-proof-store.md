### ReauthProofStore

![Core Structure](CoreView.svg)

#### Purpose

`ReauthProofStore` remembers, in memory and for a short time, that a user of an identity provider has just confirmed
their identity for one specific signature, so that the signature can be recorded once. A user of a local account
confirms a signature with the password in the same request; a user of an account at a provider confirms it by signing
in again at the provider, which ends in a different request. The store carries that confirmation from the sign-in
callback to the signing request. It does not decide whether a signature is allowed.

#### Data Model

`ReauthProofStore` is a public sealed class, registered as a singleton, so the proofs live as long as the server
process (a restart simply asks the user to confirm again). One dictionary and one lock make it thread-safe.

**Proofs**: a dictionary from the binding (user identity, project identity, review identifier, slot identity) to the
moment the proof was issued.

**Lifetime**: `TimeSpan`, 5 minutes. A proof is live while it is younger than the lifetime and ended from that moment on.

**MaxEntries**: `int` (constant), 10,000; the most proofs held at the same time.

**Clock**: a `TimeProvider`, injected so that tests control time.

#### Key Methods

**Issue**: Records that a user has just confirmed their identity for one signature.

- *Parameters*: `int userId`, `int projectId`, `string reviewId`, `int slotId`; a null review identifier raises
  `ArgumentNullException`.
- *Postconditions*: A proof for the binding exists, issued now. An older proof for the same binding is replaced, so its
  lifetime restarts. A new binding in a full store first makes room: the ended proofs are dropped, and when the store is
  still full the oldest proof is dropped.

**Has**: Tells whether a live proof exists, without spending it.

- *Returns*: `true` when the binding was confirmed and the proof has not ended. It is used by a page to show that the
  identity is confirmed and never decides whether a signature is allowed.

**TryConsume**: Spends the proof of a signature.

- *Returns*: `true` exactly once per issued proof, when it is bound to exactly these four values and has not ended;
  `false` otherwise. The proof is removed in every case where it exists (an ended one too). A try for another binding
  finds nothing and leaves the proof of the right binding alone.

#### Error Handling

A null clock or a null review identifier raises `ArgumentNullException`. Refusals are not errors: `Has` and
`TryConsume` return `false`. The store logs nothing and holds no secret, only numbers and the review identifier. The
size is bounded by `MaxEntries`: since anybody who can sign in at the provider can issue proofs, a flood can push out
older proofs (the oldest first), which only makes those users confirm again; it never makes a signature succeed.

#### Dependencies

- **TimeProvider** (.NET) — the clock.

#### Callers

- **OidcSignIn** (Identity) — calls `Issue` after a verified confirmation of identity.
- **SigningService** — calls `Has` to tell a page that the identity is confirmed, and `TryConsume` when a user of a
  provider signs; without a proof the signature is refused and recorded.
- **ServerHost** — registers the singleton.
