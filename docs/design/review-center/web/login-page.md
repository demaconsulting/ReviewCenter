### LoginPage

![Web Structure](WebView.svg)

#### Purpose

`LoginPage` is the page at `/login`. Its responsibilities are to show the sign-in form and to say that a sign-in
failed. It is the only page that does not use a live connection: it is rendered on the server as an ordinary HTML
page, and its form is posted to the login endpoint of the `Identity` subsystem (`POST /auth/login`). It is a Razor
component split into presentation (`LoginPage.razor`) and behavior (`LoginPage.razor.cs`), and it is open to
anonymous visitors.

#### Data Model

The page holds no state of its own. It reads two values from the query string of the address.

**ReturnUrl**: the address the visitor wanted before being sent to sign in (the standard `ReturnUrl` query value).

**Error**: the failure flag (`error`); the page shows the "too many attempts" message when it is `throttled` (the
constant `SignIn.ThrottledError`), the generic failure message when it has any other value, and no message when it is
absent.

**SafeReturnUrl**: derived; the return address reduced to a path of this site with `SignIn.SafeReturnUrl`, otherwise
`/`. It is written into a hidden field of the form, and the endpoint checks it again.

The form is a plain `<form method="post" action="/auth/login">` with the antiforgery token, the hidden `returnUrl`
field, and the fields `username` and `password` (the password field is masked). There is no `data-interactive` marker,
because the page has no live connection.

Every interactive element and key value has a stable `data-testid` (see the test identifier rule of the Web
subsystem): `login-page` (root), `login-form`, `login-username-input`, `login-password-input`, `login-error`,
`login-throttled`, and `login-button`.

#### Key Methods

N/A - the unit has no handlers. The form is posted by the browser, and the sign-in is performed by the `Identity`
subsystem. The failure message is "Invalid user name or password." and is the same whatever went wrong (unknown user,
wrong password, disabled account, or locked account), so the page does not reveal which user names exist. The one other
message, "Too many sign-in attempts. Try again in a few minutes.", is shown when the sign-in throttle of the `Identity`
subsystem refused the attempt. The throttle counts by client address and typed name whether or not the account exists,
so this message tells nothing about an account.

#### Error Handling

N/A - the unit contains no error-producing logic. A missing, empty, or hostile return address is replaced by `/`, and
an unknown query value is ignored.

#### Dependencies

- **SignIn** — `SafeReturnUrl` reduces the return address to one on this site, and `ThrottledError` is the value of
  the `error` query that asks for the "too many attempts" message.
- **ASP.NET Core Blazor components** — the component base class, query parameters, and the antiforgery token
  component.

#### Callers

- **App** — the router renders the page for `/login`.
- **SignIn** — the cookie handler redirects anonymous requests for protected pages, and the login endpoint redirects
  failed attempts, to this page.
- **App** (`MainLayout`) — the header has a link to this page while nobody is signed in.
