### HelpPage

#### Verification Approach

`HelpPage` is verified with component tests defined in `HelpPageTests.cs`, which use the bUnit test context to
render the real components. The index and the not-found page are rendered directly. The topic pages are found by
reflection over the route attributes of the assembly, so that a topic added to the list is tested without a new test,
and each is rendered through a dynamic component. The list of topics (`HelpTopics.All`) is the expected result, and
the tests compare the rendered links and headings with it. No mocking is applied.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- The index is headed Help and shows exactly one card for each topic of the list, each with a link to `/help/{slug}` and
  the title of the topic, and a link to `/about`.
- The number of topic pages with a fixed route under `/help/` equals the number of topics, and each topic of the
  list has a page.
- Each topic page is headed with the title of its topic, every image on it has alternative text and a source that begins
  `/help/`, and the index link points to `/help`.
- The Previous link of a topic page points to the preceding topic and is absent on the first page; the Next link
  points to the following topic and is absent on the last page.
- No page of the help contains a link that begins with `#`.
- The not-found page shows its message and a link to `/help`.

#### Test Scenarios

**HelpPage_Render_ListsEveryTopicWithItsPageLink**: The index is rendered; the heading is "Help", the number of cards
equals the number of topics, and each topic has a link to its page with its title as the text. The link to the About
page points to `/about`, and no link begins with `#`. This scenario is tested by
`HelpPage_Render_ListsEveryTopicWithItsPageLink`.

**HelpTopicPages_Render_EveryTopicHasAPageWithValidNavigation**: The pages with a route under `/help/` and no parameter
are collected; their number equals the number of topics. Each topic's page is rendered; its heading is the topic title,
each image has alternative text and a source under `/help/`, the index link points to `/help`, the Previous and Next
links point to the neighboring topics and are absent at the ends of the list, and no link begins with `#`. This scenario
is tested by `HelpTopicPages_Render_EveryTopicHasAPageWithValidNavigation`.

**HelpNotFoundPage_Render_UnknownTopic_ShowsMessageAndIndexLink**: The not-found page is rendered for a slug that is
not a topic; the message is shown and the index link points to `/help`. This scenario is tested by
`HelpNotFoundPage_Render_UnknownTopic_ShowsMessageAndIndexLink`.
