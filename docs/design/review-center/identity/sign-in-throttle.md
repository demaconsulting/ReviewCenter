### SignInThrottle

![Identity Structure](IdentityView.svg)

#### Purpose

`SignInThrottle` limits how often one client address may try to sign in, in memory. Its single responsibility is to
answer, before a password is checked, whether this attempt may go on, and to give the attempts back when a sign-in
succeeded. It does not check passwords, does not know accounts, and does not record anything. It owns the small
options type `SignInThrottleOptions`. The second step of a sign-in with a second factor (see `TotpSignIn`) is limited by
the same counters.

#### Data Model

`SignInThrottle` is an internal sealed class, registered as a singleton, so the counters live as long as the server
process. All state (one dictionary and one ordered list of the same keys) is behind one lock, so the class is
thread-safe.

**Counters**: a dictionary from a key to a window object that holds the start of a fixed window, the number of
attempts counted in it, a generation number, and the position of the key in the creation order. Two
kinds of key exist: `n|{address}|{name}` counts the attempts of one client address for one user name (the name
lower-cased with the invariant culture and cut to 64 characters, so a long name cannot make a long key), and
`a|{address}` counts all attempts of one client address. A missing or empty address is counted as the single address
`unknown`. An IPv6 address is one address as a whole (there is no grouping by prefix).

**Order**: a linked list of the keys in the order their windows were created, oldest first. Windows have a fixed
length. While the clock only moves forward the expired windows are a prefix of the list, and removing that prefix (or
the oldest ones) costs O(1) per removed key. A clock that steps backwards breaks this, because the creation order is then
not the order of the start times; so the prefix is only a cheap first step, and the whole table is scanned only when
it is full (see the table cap below).

**Generation**: a `long` taken from a counter that only grows (not even `Reset` restarts it). A window gets a new
generation when it is created, keeps it while it is counted in, and a window that replaces an expired one of the same
key gets a new one. The generation identifies the window a reservation was counted in.

**Window**: a counter whose window started more than `WindowSeconds` ago counts as 0. The window is fixed, not
sliding: it starts with the first attempt of the key and the count starts again after it ends.

**Reservation**: a small object returned by `TryBegin` and `TryBeginAddressOnly`. It holds the keys that were counted
(the name key is absent for an address-only attempt), the generation of each window, and a flag that tells whether it
has been given back.

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
- *Returns*: a reservation when the attempt may go on and has been counted; `null` when the counter of the name or
  the counter of the address has reached its limit in the current window.
- *Postconditions*: When `null` is returned nothing is counted, so a refused attempt never extends a refusal. When a
  reservation is returned both counters have been increased and the reservation names the window of each.

The windows that have passed are removed first. Only the keys that are not in the table need a new slot (a request
whose address key exists needs one slot, not two); when the table would pass `MaxKeys`, only as many of the oldest
windows as are needed are removed (never the two keys of this request), see the table cap below.

The attempt is counted before the password is checked (reserve first, give back on success), not after the failure.
Counting after the failure would let a burst of parallel requests all pass the check before any of them was counted.
Every attempt is counted the same way whether or not the user name exists, so the state of the throttle never depends
on the account.

**TryBeginAddressOnly**: Counts an attempt against the quota of the client address alone, unless the address has used
up its attempts.

- *Parameters*: `string? clientAddress` — a null value is counted as the single address `unknown`.
- *Returns*: a reservation when the attempt may go on and has been counted; `null` when the counter of the address has
  reached its limit in the current window.
- *Postconditions*: When `null` is returned nothing is counted. When a reservation is returned only the address
  counter has been increased (no name key exists), and `Succeeded` gives that one slot back, once. The table cap and
  the eviction are the same as for `TryBegin` (only the address key is protected from eviction).

It is for a request whose user name cannot be trusted, such as the callback of an identity provider (`OidcSignIn`):
counting it under one constant name would make a single counter for everybody behind a shared address.

**Succeeded**: Gives the slots of a successful sign-in back.

- *Parameters*: `Reservation reservation` — as returned by `TryBegin` or `TryBeginAddressOnly`; a null value raises
  `ArgumentNullException`.
- *Postconditions*: The reservation is marked as given back; a second call with it changes nothing. For the name
  counter (when the reservation has one) and for the address counter separately, and only when the window in the
  table is still the window the reservation was counted in (same generation): the name counter is removed, and the
  address counter is decreased by
  one (and removed when it reaches 0). A window that has been removed or started again is left alone.

A user who types the password wrong twice and then right is therefore not closer to the limit than before. Because
the address counter keeps the attempts of other names, a client that signs in correctly does not erase the attempts it
made on other names. Because hashing a password takes time, a window can end and a new one start between `TryBegin`
and `Succeeded`; the generation keeps such a late success from clearing or reducing counters that belong to later
attempts.

**ReleaseAddress**: Gives back only the address slot of an attempt, for the first step of a sign-in with a second
factor.

- *Parameters*: `Reservation reservation` — as returned by `TryBegin`; a null value raises `ArgumentNullException`.
- *Postconditions*: The reservation is marked as given back, so a later `Succeeded` or `ReleaseAddress` with it changes
  nothing. The address counter is decreased by one (and removed when it reaches 0), only when the window in the table
  is still the window the reservation was counted in (same generation). The name counter is left as it is.

A right password on an account with a second factor is not a complete sign-in, so it must not clear the counter of the
name (that would let a person who knows the password reset it again and again), but it should not use the quota of the
address either. `ReleaseAddress` does exactly that. Only a complete second step calls `Succeeded`, which clears the name
counter. A wrong code is not given back.

**Reset**: Forgets every counter. It exists for tests (a test host that is reused can start from nothing); the
application itself never calls it. Generations are not reused, so reservations from before a reset stay stale.

**Table cap**: The table holds at most `MaxKeys` (10,000) counters, so the memory use is bounded. Before a key is
added, the windows that have passed are removed from the head of the order; when the table would still pass the cap,
all keys are scanned (at most 10,000, only on this full path) and every window that has passed is removed wherever it
is in the order. When the table is still too full, only as many of the oldest windows (by creation order) as are
needed are evicted (skipping the two keys of the current request, so at most two skips). Removed windows are never
refunded to, because a reservation only matches the generation of its own window. The table is never cleared as a
whole, so one flood of fresh keys cannot reset every counter at once. Each client address needs one address key and
one name key per name it tries. Residual risk: one address creates at most
`MaxFailuresPerAddress` name keys per window, so filling the table with live counters inside one window takes a few
hundred source addresses (for example an IPv6 /64, or a forged forwarded header, see the limitations below) and about
10,000 requests. Such a distributed flood can push out older counters, including its own, so the throttle degrades
toward failing open for those clients; it never refuses anybody because of the cap, and the account lockout (kept in
the database) still applies. A clock that steps backwards leaves a window in the table until time catches up; the
limits still hold, because a counter checks its own age, and the scan of a full table removes expired windows
wherever they are in the order.

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
  address is reached sooner. With `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` the framework trusts `X-Forwarded-For`
  from any sender; the server does not restrict which proxies it trusts. Keeping the header trustworthy is the
  responsibility of the deployment: the proxy must overwrite the header and be the only route to the server.
  Otherwise a client can choose its own address and avoid the per-address and per-name limits (the lockout of the
  user name still applies). This is a known limitation that is not mitigated in the code.
- The lockout is per user name, so anyone who knows a user name can keep that account locked for 5 minutes at a time,
  also from an address that the throttle allows. The throttle limits how often one address can try; it does not
  prevent this. This is an accepted trade-off of locking by user name.
- Several users behind one shared address (an office network address) share the limit per address.
- The cap on the table evicts the oldest counters under a distributed flood (see the table cap above).

#### Dependencies

- **SignInThrottleOptions** (Identity) — the settings; validated by `SignIn.AddSignIn`.
- **TimeProvider** (.NET) — the clock; tests supply a movable one.

#### Callers

- **SignIn** — the login endpoint calls `TryBegin` before the password is checked, `Succeeded` with the
  reservation after a successful sign-in, and `ReleaseAddress` after a right password that needs a second factor;
  `AddSignIn` registers the options and the singleton.
- **TotpSignIn** — the second step calls `TryBegin` for the address and the name of the challenge and `Succeeded`
  after a complete sign-in.
- **OidcSignIn** — the callback of an identity provider calls `TryBeginAddressOnly` and `Succeeded` for an accepted
  callback.
- **ServerHost** — reads the options once at start-up to report invalid settings.
- Tests — call `Reset` and `KeyCount`.
