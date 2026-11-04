### NotificationComposer

![Core Structure](CoreView.svg)

#### Purpose

`NotificationComposer` turns the inbox of a user into the content of a notification mail. It does three things: it
reduces an inbox row to a notification candidate (the item, its section, and a state without text), it tells whether a
candidate is new for a user, and it composes the mail about the new items. The mail says that there is news and where
it is. It never says what the news is: no detail text of an inbox row, no comment, no answer, no verdict, and no
outcome of a review ever enters a mail, because a mail leaves the server and the page behind the sign-in does not. The
unit is pure: it reads no store, sends nothing, and keeps no state.

#### Data Model

The composer is made with the name of the installation, the base address of the server, and a flag that says whether
the titles of the reviews may be shown. The name is cut to 100 characters and every control character and line or
paragraph separator in it becomes a
space, so a name can not break a header line. The base address must not be empty.

A `NotificationCandidate` holds the project id, the project name, the review id, the title, the section of the inbox,
and the state. Its key is the project id, the review id, and the section; one review can be a candidate in several
sections at the same time. The state is a short text token:

- `to-answer` — for the rows "Not started" and "Draft" of the section to answer, which are one state, because starting
  an answer is no news;
- `question` — a question for the user;
- `disputed` — answers that differ; the token has no number, because a number derived from the ballots of the others
  must not leave the server;
- `ready` — a review that waits for the signature of the user;
- `sign-again` — a review that changed after the user signed;
- `lost` — a signature that no longer counts;
- `replies:N` — replies that wait for the user, with the number N;
- `concluded` — a review that was approved or rejected, which are one state, because the outcome is not told;
- `reopened` — a review that was concluded and is open again.

A `NotificationComposition` holds the subject, the plain text, and the HTML of a mail.

Limits: at most 50 items are listed, followed by "... and N more."; the text and the HTML are kept below 80,000
characters each, which is within what `NotificationMessage` accepts. The subject is `<name>: there is news for you`
for a mail about new items, and `<name>: your daily summary` for a daily summary.

#### Key Methods

**Classify**: Reduces an inbox row to a notification candidate.

- *Parameters*: The inbox row, which must not be null.
- *Returns*: The candidate, or null for the rows that must not cause a mail: the failed checks of a review, and a
  review that waits for others. The failed checks hint at the verdicts of the reviewers, and a review that waits for
  others calls for no action of the user. A row with a kind that the composer does not know also gives null.
- *Postconditions*: The candidate holds no detail text, no due date, and no read flag of the row.

**IsNew**: Tells whether a candidate is new for a user.

- *Parameters*: The state that the user was told about (null when nothing was stored), and the current state.
- *Returns*: True when nothing was stored, or when the states differ. Two states that both carry reply counts are new
  only when the count rose, because a user who answers replies must not be mailed about the replies that are left.

**Compose**: Composes the mail about the new items.

- *Parameters*: The new candidates, the number of outstanding candidates that the user was told about before and that
  still wait, and the mode (Every or Daily).
- *Returns*: A `NotificationComposition`.
- *Postconditions*: The items are grouped by the section of the inbox in the fixed order To answer, Answers that differ,
  To sign, Signature no longer counts, Replies waiting, and Updates. Each item shows the project name, the title (when
  titles are included) or the review id (when they are not), a sentence for its state, and a link. A concluded review
  reads "the review was concluded", and a reopened review reads "the review was reopened"; there is no word for the
  outcome. Replies read "1 reply is waiting" or "N replies are waiting". The link is the base address, the path
  `/projects/<project id>/reviews/<review id>` with the review id escaped, and no query and no token. A daily summary
  lists the new items first and then says how many items still wait, as a number only. The mail ends with the reason
  for the mail and a link to the Account page, where the user can stop it. In both parts every control character and
  every Unicode line or paragraph separator (U+2028, U+2029) of a text of a user becomes a space; in the HTML part
  every text is HTML-encoded and every link is encoded for the attribute.
- *Errors*: An empty list of items raises `ArgumentException`.

#### Error Handling

The unit raises `ArgumentNullException` for a missing row, `ArgumentException` for a missing list of items or an empty
base address, and nothing else. It does not log. The subject never holds the text of a user, except for the name of the
installation, which is set by the operator and cleaned.

#### Dependencies

- **InboxItem and InboxSection** — the rows of the inbox that are reduced.
- **NotificationMessage** — the limits that the composed text must keep (checked by a test).

#### Callers

- **NotificationDispatcher** — classifies the inbox of each recipient, decides what is new, and composes the mail.
