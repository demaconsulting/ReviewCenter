### SignInThrottle

![Identity Structure](IdentityView.svg)

#### Purpose

`SignInThrottle` limits how often one client address may try to sign in, in memory. Its single responsibility is to
answer, before a password is checked, whether this attempt may go on, and to give the attempts back when a sign-in
succeeded. It does not check passwords, does not know accounts, and does not record anything. It owns the small
options type `SignInThrottleOptions`.

#### Data Model

`SignInThrottle` is an internal sealed class, registered as a singleton, so the counters live as long as the server
process. All state is one dictionary behind one lock, so the class is thread-safe.

**Counters**: a dictionary from a key to the start of a fixed window and the number of attempts counted in it. Two
kinds of key exist: `n|{address}|{name}` counts the attempts of one client address for one user name (the name
lower-cased with the invariant culture and cut to 64 characters, so a long name cannot make a long key), and
`a|{address}` counts all attempts of one client address. A missing or empty address is counted as the single address
`unknown`. An IPv6 address is one address as a whole (there is no grouping by prefix).

**Window**: a counter whose window started more than `WindowSeconds` ago counts as 0. The window is fixed, not
sliding: it starts with the first attempt of the key and the count starts again after it ends.

**MaxKeys**: `int` (constant) — 10,000; the most keys held.

**SignInThrottleOptions** (supporting type): a settings object bound from the `Identity:Throttle` configuration section,
with three settings. `MaxFailuresPerName` (`int`, default 5, must be greater than 0): the attempts allowed per window
for one client address and user name. `MaxFailuresPerAddress` (`int`, default 20, must be greater than 0 and not lower
than `MaxFailuresPerName`): the attempts allowed per window for one client address over all names. `WindowSeconds`
(`int`, default 300, must be between 1 and 86400, the constant `MaximumWindowSeconds`): the length of the window. The
section is read from `appsettings.json` and from environment variables such as `Identity__Throttle__MaxFailuresPerName`.
The name of the section (`Identity`) differs from the section of the other sign-in settings (`Auth`); both exist and
neither is renamed here.

#### Key Methods

**TryBegin**: Counts a sign-in attempt, unless the client has used up its attempts.

- *Parameters*: `string? clientAddress`, `string? username` — the typed name; a null value is treated as empty.
- *Returns*: `bool` — `true` when the attempt may go on and has been counted; `false` when the counter of the name or
  the counter of the address has reached its limit in the current window.
- *Postconditions*: When `false` is returned nothing is counted, so a refused attempt never extends a refusal. When
  `true` is returned both counters have been increased.

The attempt is counted before the password is checked (reserve first, give back on success), not after the failure.
Counting after the failure would let a burst of parallel requests all pass the check before any of them was counted.
Every attempt is counted the same way whether or not the user name exists, so the state of the throttle never depends
on the account.

**Succeeded**: Gives the slots of a successful sign-in back.

- *Parameters*: `string? clientAddress`, `string? username` — as given to `TryBegin`.
- *Postconditions*: The counter of the address and name is removed, and the counter of the address is decreased by one
  (and removed when it reaches 0).

A user who types the password wrong twice and then right is therefore not closer to the limit than before. Because
the address counter keeps the attempts of other names, a client that signs in correctly does not erase the attempts it
made on other names.

**Reset**: Forgets every counter. It exists for tests (a test host that is reused can start from nothing); the
application itself never calls it.

**Table cap**: Before a key is added and the table would pass `MaxKeys`, keys whose window has passed are removed; when
the table is still too full it is cleared. The throttle therefore fails open when a flood of distinct names fills it,
for the benefit of a bounded memory use and of users who are not flooding; the account lockout is kept in the database
and still applies. Each client address needs one address key and one name key per name it tries.

#### Error Handling

A null `IOptions` or `TimeProvider` raises `ArgumentNullException` when the class is created. A null address or name is
not an error. The settings are validated when the server starts (see the `SignIn` design and the `ServerHost` design);
the throttle assumes valid values. Nothing is logged: the throttle holds no passwords and no names beyond the
lower-cased, cut keys in memory.

Documented limitations, all deliberate:

- The counters are in memory in one process and are lost when the server restarts. With more than one server process
  each has its own counters, so the limits multiply by the number of processes; the throttle is built for the single
  instance that the tool is run as.
- The throttle limits by client address. Without a forwarded-headers setup the address is the address of the direct
  connection, which behind a reverse proxy is the proxy: all users then share one set of counters, and the limit per
  address is reached sooner. With `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` the address is taken from the
  `X-Forwarded-For` header. The framework then trusts that header from any sender, so the proxy must set or overwrite
  it and the server must be reachable only through the proxy; otherwise a client can choose its own address and avoid
  both limits (the account lockout still applies).
- Several users behind one shared address (an office network address) share the limit per address.
- The cap on the table fails open (see above).

#### Dependencies

- **SignInThrottleOptions** (Identity) — the settings; validated by `SignIn.AddSignIn`.
- **TimeProvider** (.NET) — the clock; tests supply a movable one.

#### Callers

- **SignIn** — the login endpoint calls `TryBegin` before the password is checked and `Succeeded` after a successful
  sign-in; `AddSignIn` registers the options and the singleton.
- **ServerHost** — reads the options once at start-up to report invalid settings.
- Tests — call `Reset` and `KeyCount`.
