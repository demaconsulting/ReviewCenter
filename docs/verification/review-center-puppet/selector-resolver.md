## SelectorResolver

### Verification Approach

`SelectorResolver` is verified with unit tests defined in `SelectorResolverTests.cs`. The class is a stateless
function, so the tests call `SelectorResolver.Resolve` with representative inputs and assert on the returned text or
on the thrown `ArgumentException`. No mocks are needed.

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All unit tests pass with zero failures.
- A plain name and a `testid:` name resolve to a `data-testid` attribute selector, with quotes escaped.
- `css=`, `text=`, `role=`, and CSS selectors pass through unchanged.
- Empty and white-space selectors are rejected.

### Test Scenarios

**SelectorResolver_Resolve_AllForms_ReturnsPlaywrightSelector**: `hello-button` and `testid:hello-button` resolve to
`[data-testid="hello-button"]`, a quote in a test id is escaped, and `css=.button`, `text=Count: 2`,
`role=button[name="Save"]`, and `#main` are returned exactly as given. Tested by
`SelectorResolver_Resolve_AllForms_ReturnsPlaywrightSelector`.

**SelectorResolver_Resolve_Blank_ThrowsArgumentException**: An empty selector and a selector of two spaces each
cause an `ArgumentException`. Tested by `SelectorResolver_Resolve_Blank_ThrowsArgumentException`.
