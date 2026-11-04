### NotificationComposer

#### Verification Approach

`NotificationComposer` is verified with unit tests defined in `NotificationComposerTests.cs`. The unit is pure, so the
tests call it directly: they build inbox rows and candidates, compose messages, and check the subject, the plain text
part, and the HTML part. No stand-in and no database are needed. The tests that matter for security use sentinel
texts: a detail sentence that must not appear anywhere in a message, a title with a line break and a title with markup,
an instance name with a line break, and a review id with a quote and with special characters. A test builds the largest
message that the composer can make and checks that `NotificationMessage` accepts it.

#### Test Environment

N/A - standard test environment (no special fixtures).

#### Acceptance Criteria

- All tests pass with zero failures.
- The subject is a fixed text with the name of the installation, for a message about new items and for a daily summary,
  holds no text of an item, and stays on one line.
- The rows of the inbox are classified into the states of the rules, the failed-checks row and the waiting-for-others
  row give no candidate, and a missing row is refused.
- An item is new when nothing was stored or the state differs, is not new when the state is the same, and waiting
  replies are new only when their number rises.
- The kind of an item is the part of its state before the first colon, and a question and a standing item to answer
  have different keys.
- No detail text, no outcome word, and no number of disputed answers appears in a message.
- Each item shows the project, the title or the review id, and a link that has the form of the review links, starts with
  the base address, is escaped, and has no query.
- Text of a user cannot change the structure of the plain text part or the HTML part.
- The items are grouped by section, at most 50 are listed, the daily summary counts the outstanding items, and the
  footer links to the Account page.
- The largest message is accepted by `NotificationMessage`.
- A message without an item and an empty base address are refused.

#### Test Scenarios

**Subject**: The subject of a message about new items and of a daily summary is the fixed text with the name of the
installation, no text of an item is in it, and a control character in the name becomes a space. These scenarios are
tested by `NotificationComposer_Compose_Every_SubjectIsFixedText`, `NotificationComposer_Compose_Daily_SubjectIsFixedText`,
`NotificationComposer_Compose_AnyItems_SubjectHasNoItemText`, and
`NotificationComposer_Compose_InstanceNameWithLineBreak_SubjectSingleLine`.

**Classification of the states**: Each kind of row of the inbox gives its state: "Not started" and "Draft" the same
state, a question, a disputed row without a count, a "Not signed" row as ready to sign, a "sign again" row, a lost
signature, waiting replies with their count, an approved and a rejected update as concluded, and a reopened update.
These scenarios are tested by `NotificationComposer_Classify_NotStartedAndDraft_SameState`,
`NotificationComposer_Classify_QuestionChip_QuestionState`, `NotificationComposer_Classify_Disputed_StateHasNoCount`,
`NotificationComposer_Classify_NotSigned_ReadyState`, `NotificationComposer_Classify_SignAgain_SignAgainState`,
`NotificationComposer_Classify_SignatureLost_LostState`, `NotificationComposer_Classify_RepliesWaiting_StateHasCount`,
`NotificationComposer_Classify_Approved_Concluded`, `NotificationComposer_Classify_Rejected_Concluded`, and
`NotificationComposer_Classify_Reopened_Reopened`.

**Rows that cause no mail**: A failed-checks row and a waiting-for-others row give no candidate. This scenario is tested
by `NotificationComposer_Classify_FailedChecks_ReturnsNull` and
`NotificationComposer_Classify_WaitingForOthers_ReturnsNull`.

**Missing row**: A missing row raises `ArgumentNullException`. This scenario is tested by
`NotificationComposer_Classify_NullRow_ThrowsArgumentNull`.

**What is new**: With nothing stored an item is new, with a different state it is new, with the same state it is not
new, and waiting replies are new when their number rises and not new when it falls or stays. This scenario is tested by
`NotificationComposer_IsNew_StoredAndCurrentState_FollowsRules`.

**What is lowered**: Waiting replies are lowered only when both states are counts of waiting replies and the current
number is smaller than the stored one; nothing stored, the same number, a higher number, another kind of state, and a
state that is not a count are not lowered. This scenario is tested by
`NotificationComposer_IsLowered_StoredAndCurrentState_OnlyFallingReplies`.

**Kind of an item**: The kind of a state is the part before the first colon, so a state with a count of waiting replies
has the kind without the count, and a state without a colon is its own kind. A question and a standing item to answer
of one review have different keys. These scenarios are tested by `NotificationComposer_KindOf_StateWithCount_WithoutCount`,
`NotificationComposer_KindOf_StateWithoutCount_TheState`, and
`NotificationComposer_Classify_QuestionAndStanding_DifferentKeys`.

**No detail and no outcome**: A sentinel detail sentence of an inbox row is in neither part, the words Approved and
Rejected are nowhere in a message about updates, a concluded review reads "the review was concluded", a reopened review
reads "the review was reopened", and a disputed item shows no number. These scenarios are tested by
`NotificationComposer_Compose_DetailSentinel_NotInTextOrHtml`, `NotificationComposer_Compose_Updates_NoOutcomeWord`,
`NotificationComposer_Compose_Concluded_SaysWasConcluded`, `NotificationComposer_Compose_Reopened_SaysWasReopened`, and
`NotificationComposer_Compose_Disputed_NoCountInBody`.

**Item content**: Waiting replies show their number, each item shows the name of its project, the title is shown when
titles are included, and with titles left out the title is absent and the review id is shown. These scenarios are tested
by `NotificationComposer_Compose_Replies_ShowsCount`, `NotificationComposer_Compose_Item_ShowsProjectName`,
`NotificationComposer_Compose_IncludeTitlesTrue_ShowsTitle`, `NotificationComposer_Compose_IncludeTitlesFalse_OmitsTitle`,
and `NotificationComposer_Compose_IncludeTitlesFalse_ShowsReviewId`.

**Links**: The link of an item has the form of the review links of the pages, starts with the base address, has a
single slash when the base address ends with a slash, has the review id escaped, and has no query. These scenarios are
tested by `NotificationComposer_Compose_Item_LinkMatchesReviewLinks`,
`NotificationComposer_Compose_Item_LinkStartsWithBaseUrl`, `NotificationComposer_Compose_BaseUrlTrailingSlash_SingleSlash`,
`NotificationComposer_Compose_ReviewIdWithSpecialChars_LinkEscaped`, and `NotificationComposer_Compose_Item_LinkHasNoQuery`.

**Text of a user**: A title with a line break stays on one line in the plain text part, the Unicode line and paragraph
separators in a title become spaces in both parts, markup in a title is encoded in the HTML part, and a quote in a
review id cannot leave the href attribute. These scenarios are tested by
`NotificationComposer_Compose_TitleWithLineBreak_TextPartSingleLine`,
`NotificationComposer_Compose_TitleWithUnicodeLineSeparators_BecomeSpaces`,
`NotificationComposer_Compose_TitleWithMarkup_HtmlEncoded`, and
`NotificationComposer_Compose_ReviewIdWithQuote_HrefEncoded`.

**Layout**: The items are grouped by section in the order of the inbox, a daily summary lists the new items before the
count of the outstanding items and shows that count as a number only, a message of the mode Every has no line about
outstanding items, 60 items give 50 listed items and a line with the rest, the line with the rest states how many items
were not listed, the footer links to the Account page, and the footer says why the mail was sent. These scenarios are
tested by `NotificationComposer_Compose_MixedSections_GroupedInOrder`,
`NotificationComposer_Compose_Daily_NewItemsThenOutstanding`, `NotificationComposer_Compose_Daily_OutstandingNotListed`,
`NotificationComposer_Compose_Every_NoOutstandingLine`, `NotificationComposer_Compose_60Items_50ListedAndMore`,
`NotificationComposer_Compose_60Items_StatesHowManyNotListed`, `NotificationComposer_Compose_Footer_LinksToAccount`,
and `NotificationComposer_Compose_Footer_SaysWhyMailWasSent`.

**Largest message**: The largest message, with the most items and the longest titles, is accepted by
`NotificationMessage`. This scenario is tested by `NotificationComposer_Compose_MaxItemsMaxLengthTitles_MessageAccepted`.

**Refused input**: A message without an item raises `ArgumentException`, and an empty base address is refused. These
scenarios are tested by `NotificationComposer_Compose_NoItems_ThrowsArgument` and
`NotificationComposer_Constructor_EmptyBaseUrl_Throws`.
