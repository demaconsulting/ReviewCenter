### HomePage

#### Verification Approach

`HomePage` is verified with component tests defined in `HomePageTests.cs`, which use the bUnit
test context to render the real component and drive its buttons. bUnit runs the component
life-cycle, including `OnAfterRender`, in the same way a live connection does. Tests locate
elements by their `data-testid` attributes and assert on the rendered markup. No mocking is
applied.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- The page shows the product title and no greeting before any interaction.
- Clicking Hello shows the greeting, and clicking it again still shows a single greeting.
- The counter starts at 0 and increases by 1 per click of the counter button.
- The interactive marker is `true` after the page has rendered on a live connection.

#### Test Scenarios

**HomePage_Render_Default_ShowsTitle**: The page is rendered; the heading text "ReviewCenter"
is present. This scenario is tested by `HomePage_Render_Default_ShowsTitle`.

**HomePage_Render_Default_ShowsNoGreeting**: The page is rendered; the greeting element does not
exist yet. This scenario is tested by `HomePage_Render_Default_ShowsNoGreeting`.

**HomePage_HelloButton_Clicked_ShowsGreeting**: The page is rendered and the Hello button is
clicked; the greeting "Hello from ReviewCenter!" appears. This scenario is tested by
`HomePage_HelloButton_Clicked_ShowsGreeting`.

**HomePage_HelloButton_ClickedTwice_ShowsSingleGreeting**: The page is rendered and the Hello
button is clicked twice; exactly one greeting is shown. This scenario is tested by
`HomePage_HelloButton_ClickedTwice_ShowsSingleGreeting`.

**HomePage_Render_Default_CounterStartsAtZero**: The page is rendered; the counter shows 0. This
scenario is tested by `HomePage_Render_Default_CounterStartsAtZero`.

**HomePage_CounterButton_ClickedThreeTimes_ShowsCountThree**: The page is rendered and the
counter button is clicked three times; the counter shows 3. This scenario is tested by
`HomePage_CounterButton_ClickedThreeTimes_ShowsCountThree`.

**HomePage_AfterRender_Interactive_SetsMarker**: The page is rendered with the life-cycle that
a live connection uses; the `data-interactive` attribute of the root element is `true`. This
scenario is tested by `HomePage_AfterRender_Interactive_SetsMarker`.
