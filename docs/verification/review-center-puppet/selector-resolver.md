## SelectorResolver

### Verification Approach

`SelectorResolver` is verified with unit tests defined in `SelectorResolverTests.cs`. The class is a stateless
function, so the tests call `SelectorResolver.Resolve` with representative inputs and assert on the returned text or
on the thrown `ArgumentException`. No mocks are needed.

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All unit tests pass with zero failures.
- A plain name resolves to a `data-testid` attribute selector.
- CSS, text, and chained selectors pass through unchanged.
- Empty and white-space selectors are rejected.

### Test Scenarios

**SelectorResolver_Resolve_PlainName_UsesDataTestId**: The name `hello-button` resolves to
`[data-testid="hello-button"]`. This scenario is tested by `SelectorResolver_Resolve_PlainName_UsesDataTestId`.

**SelectorResolver_Resolve_SpecialCharacters_PassesThroughUnchanged**: The selectors `#main`, `.button`,
`text=Hello`, `button >> nth=0`, and `[data-testid='x']` are returned exactly as given. This scenario is tested by
`SelectorResolver_Resolve_SpecialCharacters_PassesThroughUnchanged`.

**SelectorResolver_Resolve_Blank_ThrowsArgumentException**: An empty selector and a selector of two spaces each
cause an `ArgumentException`. This scenario is tested by `SelectorResolver_Resolve_Blank_ThrowsArgumentException`.
