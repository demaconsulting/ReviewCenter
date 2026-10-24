### PasswordHasher

![Identity Structure](IdentityView.svg)

#### Purpose

`PasswordHasher` implements the Core interface `IPasswordHasher` on top of the password hasher of the ASP.NET Core
shared framework. Its single responsibility is to turn a password into a hash that can be stored and to check a
password against such a hash, so that `UserService` in the `Core` subsystem can protect passwords without a reference
to the web framework.

#### Data Model

The unit is a stateless class that holds one framework hasher (`PasswordHasher<string>`; the user argument of the
framework hasher is unused, so a plain string stands in for the user type). The hash is a self-describing text: it
carries the algorithm version, the PBKDF2 iteration count, and the random salt, so a later increase of the work factor
does not invalidate stored hashes.

#### Key Methods

**Hash**: Hashes a password.

- *Parameters*: `string password`.
- *Returns*: `string` — the hash; a new random salt is used on every call, so hashing the same password twice gives
  two different texts.
- *Preconditions*: `password` is not null.
- *Postconditions*: The hash never contains the password.

**Verify**: Checks a password against a stored hash.

- *Parameters*: `string hash` — a text returned by `Hash`; `string password`.
- *Returns*: `bool` — `true` when the password matches.
- *Preconditions*: Neither argument is null.

A match that the framework would like to upgrade (`SuccessRehashNeeded`, because the stored work factor is lower than
the current one) is a success; the hash is not replaced. This is a deliberate simplification.

#### Error Handling

A null argument raises `ArgumentNullException`. A wrong password is not an error: `Verify` returns `false`. Nothing else
is handled here; failures of the framework propagate unchanged. Nothing is logged: the password and the hash must
never be written to a log or a message.

#### Dependencies

- **IPasswordHasher** (Core) — the interface implemented.
- **ASP.NET Core shared framework** — `Microsoft.AspNetCore.Identity.PasswordHasher<TUser>` from
  `Microsoft.Extensions.Identity.Core`; see the ASP.NET Core integration design in the OTS chapter.

#### Callers

- **UserService** — hashes new passwords and checks passwords at sign-in and when a password is changed, through
  the Core interface.
- **ServerHost** — registers the adapter as the implementation of the Core interface.
