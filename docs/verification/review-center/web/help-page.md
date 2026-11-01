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
  key, the certificate store and File provider settings, the expiry warning (`Attestation:ExpiryWarningDays`), and
  `Attestation:RequireTrustedKey` (refusal to start for a self-signed key or a
  certificate without a signing key usage, and the review page reason for a certificate that stops being valid while
  the server runs).
- The API tokens topic describes how to create a token on the account page, that the value is shown once, the expiry
  choices and the limit, a curl example with the bearer header, the endpoints and the OpenAPI document, that a token
  never signs, that a review made through the API is a Draft marked as created via API, and the 401 and 429 answers.
- The Project members topic describes the + Add member button and the exact user name, the temporary password that is
  shown once, the rule of the last project admin, the marker for a person who is no longer a member, and the Audit trail
  button, and is in the list of topics at `/help/project-members`.
- The Evidence settings topic has the heading "Evidence settings", shows its two screenshots from `/help/` with
  alternative text, and describes who can change the settings (a project admin or a system administrator), the two
  anonymous switches (independent, with the use for ReviewMark), the warning to think before turning them on, the five
  content switches, the regulatory warning, the Content scope section, and the audit trail, and contains no link that
  begins with `#`.
- The Getting started, Reviewers and signing, and API tokens topics each mention the identity provider.
- The Reviewers and signing topic says that the person who reopened a review cannot sign it again while the project
  rule is on, and that a reopen that would leave a slot without a possible signer is refused.
- The Reviewers and signing topic says that a Fail answer does not block signing, explains the Rejected outcome and that
  it needs a conclusion text, and no longer says that a Fail answer blocks signing; the Reading a review topic says that
  a slot owner writes the conclusion in the Review conclusion card and that a concluded (approved or rejected) review is
  locked.
- The Two-factor sign-in topic follows the API tokens topic in the list, has its title, a screenshot with alternative
  text from `/help/`, and describes setting it up, the ten recovery codes shown once, signing in, recovery, signing a
  review (a recovery code cannot sign and a used code needs the next one), turning off, the administrator reset (not for
  project administrators), that open sessions stay valid, and the setting `Identity:Totp:Enabled` with the warning that
  accounts are then protected by the password alone; the signing topic links to it.
- The Checklist template topic follows the Evidence settings topic in the list, has the heading "Checklist template",
  and describes who can change the template, the built-in default, that only later reviews use a new template while
  existing reviews keep theirs, the export, the import limit of 256 KiB, the validation, the replace and the reset, the
  rule of another version label, the audit action `project.checklist-changed`, and the IEC 62304 example with the note
  that a template does not make a record compliant; it contains no link that begins with `#`.
- The Inbox topic follows the Getting started topic in the list (second), explains the four groups, the New marker and
  Mark all as read, and links to the inbox.

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
settings `Attestation:Provider`, `Attestation:CertificatePath` and `Attestation:ExpiryWarningDays`, and
`Attestation:RequireTrustedKey`. It says that `RequireTrustedKey` makes the server
refuse to start for a self-signed key or a certificate without a signing key usage, that a certificate that stops being
valid while the server runs is reported on the review page instead of the button, and that the note of a self-signed
certificate that the administrator supplied says that the administrator supplied it. It also says that the signature
certifies the
document with no changes allowed, that a certificate for code signing only is used, not refused for that reason, with
a note in the PDF and a start-up warning, that the PDF lists who answered each check and when, and that the server
does not check the certificate chain or revocation. This
scenario is tested by `HelpAttestationPage_Render_ExplainsWhatTheSignatureProvesAndTheKey`.

**HelpSigningPage_Render_ExplainsTheReopenerRule**: The Reviewers and signing page is rendered. The text says that,
while the project rule is on, the person who reopened a review cannot sign it again, and that a reopen that would leave
a slot without anyone who may sign is refused; no link begins with `#`. This scenario is tested by
`HelpSigningPage_Render_ExplainsTheReopenerRule`.

**HelpPage_Signing_NoLongerSaysFailBlocks**: The Reviewers and signing page is rendered. The text says that a Fail
answer does not block signing, names the Rejected outcome, and does not contain the old sentence about an answer of
Fail that blocks signing. This scenario is tested by `HelpPage_Signing_NoLongerSaysFailBlocks`.

**HelpPage_ReadingReview_ShowsSlotOwnerNote**: The Reading a review page is rendered. The text tells a person who holds
a reviewer slot that they own a ballot, answer the checks in it, submit it and then sign, and that the review concludes
when every slot has signed the same, current record; it says that a concluded (approved or rejected) review is locked,
and tells a person who holds a slot of the review that the conclusion is written in the Review conclusion card. This
scenario is tested by `HelpPage_ReadingReview_ShowsSlotOwnerNote`.

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

**HelpMembersPage_Render_ExplainsAddingAccountsAndTheLastAdminRule**: The Project members page is rendered; the text
names the + Add member button and the exact user name, says that a temporary password is shown once, that the last
project admin cannot be removed, that a person who left shows "No longer a project member", and names the Audit trail
button, and the list of topics holds the address `/help/project-members`. This scenario is tested by
`HelpMembersPage_Render_ExplainsAddingAccountsAndTheLastAdminRule`.

**HelpEvidenceSettingsPage_Render_ExplainsAnonymousAccessContentSwitchesAndScope**: The Evidence settings page is
rendered; the heading is "Evidence settings", the two screenshots `/help/evidence-settings-1.png` and
`/help/evidence-settings-2.png` are shown in this order, each with alternative text, and the text names the project
admin and the system administrator, the switches Anonymous evidence index and Anonymous attestation PDFs, says that they
are independent, names ReviewMark, and contains "Think before you turn them on". It names the five content switches
by their titles, mentions the regulatory warning, the Content scope section, and the audit trail, and no link begins
with `#`. This scenario is tested by `HelpEvidenceSettingsPage_Render_ExplainsAnonymousAccessContentSwitchesAndScope`.
The list of topics holds the topic at `/help/evidence-settings`, which
`HelpTopicPages_Render_EveryTopicHasAPageWithValidNavigation` checks.

**HelpChecklistTemplatePage_Render_ExplainsExportImportReplaceResetAndCopies**: The Checklist template page is rendered;
the topic follows Evidence settings in the list, the heading is "Checklist template", and the text names the project
admin, the built-in default, that only the reviews created afterwards use the new template and that existing reviews
keep exactly the checklist they were created with. It names the steps Export current checklist, Validate, Replace
checklist, and Reset to built-in default, the rule of another version label, the limit of 256 KiB, the audit action
`project.checklist-changed`, and the IEC 62304 example with the note that a template does not make a record compliant;
no link begins with `#`. This scenario is tested by
`HelpChecklistTemplatePage_Render_ExplainsExportImportReplaceResetAndCopies`.

**HelpTopicPage_Render_MentionsSignInWithProvider**: A theory over the topics `getting-started`,
`reviewers-and-signing`, and `api-tokens` (in `OidcPageTests.cs`); each page is found by its route and rendered, and
its markup contains the words "identity provider". This scenario is tested by
`HelpTopicPage_Render_MentionsSignInWithProvider`.

**HelpTwoFactorPage_Render_ExplainsEnrollingSigningInRecoveryAndSigning**: The topic `two-factor` follows `api-tokens`
in the list; its page is rendered together with the signing topic. The heading is "Two-factor sign-in", the screenshot
`/help/two-factor-1.png` has alternative text, and the text names the steps to set up, the 10 recovery codes shown
once, the use of a recovery code, that recovery codes cannot sign, and that a used code needs the next one; it names
turning off, the reset by an administrator that project administrators cannot do, that open sessions stay valid, and
the setting `Identity:Totp:Enabled` with the warning about the password alone. The signing topic contains the link
`/help/two-factor`, and no link begins with `#`. This scenario is tested by
`HelpTwoFactorPage_Render_ExplainsEnrollingSigningInRecoveryAndSigning`.

**HelpInboxPage_Render_ExplainsSectionsUnreadAndMarkAllRead**: The topic `inbox` is listed second, after the first
topic. Its page is rendered; the text names the four groups (To sign, Replies waiting, Failed checks, and Updates), the
New marker, and Mark all as read, and it links to `/inbox`. This scenario is tested by
`HelpPage_HelpInboxPage_Render_ExplainsSectionsUnreadAndMarkAllRead`.

**HelpPage_SigningCommentsInbox_UsePlainLanguageAndExplainWhereAreWe**: The topics on signing, comments and the inbox
are rendered. The signing topic explains the "Where are we?" panel and the words "Signed, then the review changed",
and says that reviewers look again and sign again; none of the three topics contains the words "out of date" or
"out-of-date". This scenario is tested by `HelpPage_SigningCommentsInbox_UsePlainLanguageAndExplainWhereAreWe`.
