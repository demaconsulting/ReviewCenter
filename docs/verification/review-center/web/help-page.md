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
- The Signed attestation topic describes the download, what the signature proves and does not prove, the self-signed
  key, the certificate store settings, and `Attestation:RequireTrustedKey` (refusal to start for a self-signed key or a
  certificate without a signing key usage, and the review page reason for a certificate that stops being valid while
  the server runs).
- The API tokens topic describes how to create a token on the account page, that the value is shown once, the expiry
  choices and the limit, a curl example with the bearer header, the endpoints and the OpenAPI document, that a token
  never signs, that a review made through the API is a Draft marked as created via API, and the 401 and 429 answers.

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

**HelpAttestationPage_Render_ExplainsWhatTheSignatureProvesAndTheKey**: The Signed attestation page is rendered; the
heading is "Signed attestation", and the text names the button "Download attestation (PDF)", says that the signature
does not prove who the reviewers are, shows the label "SELF-SIGNED KEY - NOT AN ORGANIZATION KEY", and names the
settings `Attestation:Provider` and `Attestation:RequireTrustedKey`. It says that `RequireTrustedKey` makes the server
refuse to start for a self-signed key or a certificate without a signing key usage, that a certificate that stops being
valid while the server runs is reported on the review page instead of the button, and that the note of a self-signed
certificate from the store says that the administrator supplied it. This scenario is tested by
`HelpAttestationPage_Render_ExplainsWhatTheSignatureProvesAndTheKey`.

**HelpApiTokensPage_Render_ExplainsTokensCurlEndpointsAndErrors**: The API tokens page is rendered; the heading is
"Automation and API tokens", the screenshot is served from `/help/` with alternative text, and the text names the
Account page, the expiry choices of 30, 90, or 365 days with 90 as the default, that the value is shown once, the limit
of 20 active tokens, and the revocation. It says that a token can never sign, shows a curl example with the bearer
header
against `/api/projects`, names `POST /api/projects/{id}/reviews`, the evidence index, and `/api/openapi.json`, explains
the use with ReviewMark (HTTP Basic, the token as the password), says that a review made through
the API is a Draft that is marked as created via API, and explains the 401 and 429 answers. No link begins with `#`.
This
scenario is tested by `HelpApiTokensPage_Render_ExplainsTokensCurlEndpointsAndErrors`.
