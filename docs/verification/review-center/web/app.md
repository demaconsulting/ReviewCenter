### App

#### Verification Approach

`App` is verified with tests defined in `AppTests.cs`. The shell is verified by starting the real
server pipeline on an in-memory server and requesting the root document. The router (`Routes`)
is verified with component tests that set the navigation address and render the component in a
test context. No mocking is applied.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All tests pass with zero failures.
- The root document contains the shell elements: head outlet content, the router output, and
  the client script reference.
- The router renders the home page for the root URL.
- The router renders the not-found message for a URL that no page handles.

#### Test Scenarios

**App_Render_ServedAtRoot_ContainsShellElements**: The pipeline is started in memory and the
root document is requested; the response contains the shell elements. This scenario is tested by
`App_Render_ServedAtRoot_ContainsShellElements`.

**Routes_Render_RootUrl_RendersHomePage**: The navigation address is set to the root URL and
`Routes` is rendered; the home page is shown. This scenario is tested by
`Routes_Render_RootUrl_RendersHomePage`.

**Routes_Render_UnknownUrl_ShowsNotFoundMessage**: The navigation address is set to a URL that
no page handles and `Routes` is rendered; the not-found message is shown. This scenario is
tested by `Routes_Render_UnknownUrl_ShowsNotFoundMessage`.
