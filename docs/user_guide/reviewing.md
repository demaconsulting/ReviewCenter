# Reviewing

This chapter describes the daily work of a review: projects and their reviews, planning, comments, the checklist,
the signatures, the lock of an approved review and how an administrator reopens it, and the signed attestation that
proves an approval.

## Projects and Reviews

A project groups the reviews of one body of work. A review is a named set of files whose content is stored
unchanged, together with a fingerprint of that content.

1. Open `/projects` (the Projects link in the header). The list shows the projects you can see: all projects for a
   system administrator, and for anyone else only the projects they are a member of (the page explains when there are
   none). A system administrator creates a project: press **+ New project**. On the form at `/projects/new`, enter
   a project name (up to 100 characters) and an optional description (up to 1000 characters), and press the create
   button. The new project opens, with no members. **Cancel** goes back to the list; select a project to open its page.
   A project that you cannot see is reported as "Project N does not exist."

   ![The Projects page with the projects that the signed-in user can see.](images/projects-list.png)
2. A project administrator (or a system administrator) manages the members on the project page, in the **Members**
   section; see Managing Project Members in the Administration chapter. The project page also
   has a **Repositories** button for the project's git repositories (see
   Repositories and Credentials in the Administration chapter).
3. On the project page, press **+ New review**. On the form at `/projects/{id}/reviews/new`, enter a review
   identifier (1 to 100 letters, digits, `.`, `_`, or `-`, starting with a letter or digit; unique within the
   project; the identifier `new` is reserved and is refused) and a title (up to 200 characters), choose the files to
   review, and press the create button. The chosen files are listed with their sizes before you submit. **Cancel**
   goes back to the project.

   ![The New review form with three chosen files listed before the review is created.](images/new-review-upload.png)
4. The review page opens. From the top it shows the title, a line that says what to do next, the files with their
   size and open comment threads, and the thread index. Select a file to read
   its content as numbered lines (see [Review Comments](#review-comments)); the page scrolls to the code. Binary
   files, and files that are not valid UTF-8 text, show a notice instead of their content. At the bottom, the
   collapsed "Details" section holds the identifier, state (`Draft`; the only state in this release), the review
   fingerprint, and the SHA-256 hash of each file; open it when you need them.

The review fingerprint is calculated exactly as in the ReviewMark tool (see the "Fingerprinting" section of the
ReviewMark User Guide): each file is hashed with SHA-256, the hashes are sorted, and the sorted hashes are hashed
again. It depends only on the content of the files, not on their names or order, and it is not affected by line
ending conversion because the bytes are never changed.

Error messages (for example a blank name, an identifier already used in the project, or too many files) are shown
on the page, and nothing is created.

### Upload Limits

- At most 200 files per review, 2 MiB per file, and 20 MiB for all files of a review.
- Only the file name of each chosen file is kept (not its folder), so two files with the same name cannot be in the
  same review; the page reports this when you submit.

### Reviews from a Repository

Instead of uploading files you can review what changed in a git repository that belongs to the project (a project
administrator or a system administrator adds it; see Repositories and Credentials in the Administration
chapter).
On the new review form (**+ New review** on the project
page), under **Files come from**, choose
**From a repository** and enter:

- **Repository**: one of the enabled repositories of this project, by name. If there is none, the page says so and
  offers only the upload.
- **Head revision**: a branch, a tag, or a commit (for example `main`, `v2.0`, or a commit id).
- **Baseline revision** (optional): the revision to compare with. Without it every file of the head revision that
  matches the selection counts as Added.
- **Selection**: the files to review, patterns one per line (default `**/*`); see Selecting Files in the
  Administration chapter.
  For example, the two lines `src/**/*.cs` and `!src/**/Generated/**` select the C# files below `src` except those in
  `Generated` folders. The patterns are applied to the files that **changed** between the two revisions, not to every
  file of the head.

When you create the review, the server fetches the repository, resolves both revisions to commits, and stores in the
database, for every changed file that matches the selection, its path, its change kind (**Added**, **Modified**, or
**Deleted**; a rename counts as a delete and an add), and its baseline and head content. After that, nothing is read
from the repository: later commits, a force push, or disabling the repository do not change the review.

- If no changed file matches, no review is created and the page says "Nothing to review: no files matching the
  selection changed between X and Y".
- A revision that git does not know, a repository that cannot be reached, or a file over the size limits is reported
  on the page and nothing is created. The limits are those of an upload (200 files, 2 MiB per file, 20 MiB per
  review) and count the baseline and head content of the changed files.
- Symbolic links and submodules are not reviewed. Those that match the selection are listed once, right after the
  review is created, and are counted in the audit entry.
- A revision such as `HEAD~1` is not accepted; use a branch, a tag, or a commit id.

On the review page, each file shows its change kind. Select a file to read it. For a file of a review that has a
baseline commit the page opens on a **Diff** view, with a **Head** and a **Baseline** button next to it (an **Added**
file has no Baseline button because there was no earlier version, and a **Deleted** file has no Head button). The
Diff view shows "x added, y removed",
then the changed lines side by side: the earlier version (baseline) on the left and the new version (head) on the
right, each with its own line numbers. Removed lines are red with a `-` and added lines green with a `+`; a run of
removed lines sits next to the run of added lines that replaces it, and a cell with nothing opposite is left empty
and grey. Unchanged lines show on both sides. Unchanged lines far from a change are collapsed behind a **Show N
unchanged lines** button, and the page scrolls to the first change when you open the file. On a narrow screen, such as
a phone, each row stacks: the baseline cell above the head cell, with the empty cells hidden. To comment on a line,
point at the line (or tab to it) and click the **+** that appears at the start of its cell (on a touch screen it is
always shown): a form opens under the row saying what you are commenting on, for example
"Comment on line 10 (before)". The **+** in the left cell comments on the baseline with the baseline line number, and
the **+** in the right cell on the head with the head line number; hold Shift while you click another line on the
same side, or fill in "Through line", to comment on a range. **Cancel** closes the form and **Comment on the whole
file instead** keeps your text. Threads appear right under the row of the lines they are about, across the full
width (a resolved thread is one line until you open it), and the thread list names them
"Before: line N" or "After: line N" and jumps to the line when you choose one. **Head** is the numbered view with
comments, and **Baseline** shows the earlier content read-only. A file that is too large to compare line by line
(more than 10,000 lines on either side, or more than 2,000 added and removed lines together) says so and offers Head
and Baseline, as the file has them. A **Deleted** file opens on a Diff view of its removed lines; you can comment on
them or on the whole file. An **Added** file opens on a Diff of all its lines as added (the left side is empty) and
you can comment on any of its lines or on the whole file; every file of an upload, and of a review created without a
baseline revision, shows the numbered lines as before. The
review fingerprint covers the files that
exist at the head (Added and Modified), exactly as for an upload. The Details section shows the repository name, the
baseline and head commits, and the selection.

### Review-Sets in the Repository

A repository can state which files are reviewed together in the `reviews:` list of the `.reviewmark.yaml` file in its
root (the file that ReviewMark reads). ReviewCenter reads only the `id`, `title`, and `paths` of each entry and
ignores every other key. A `paths` list uses the same pattern syntax as the selection above, with `!` to exclude.

```yaml
reviews:
  - id: Core
    title: Review of the Core subsystem
    paths:
      - "src/Core/**/*.cs"
      - "!src/Core/Generated/**"
```

The file is read from the head revision of the repository, so it is the version you are about to review. The limits
are: the file is at most 256 KiB, at most 500 review-sets, an `id` of at most 100 characters, a `title` of at most 200,
at most 100 paths in a review-set, and at most 400 characters in a path. The `id` and `title` are required, must not
contain control characters, and the ids must be different. A file that is not valid YAML, uses YAML aliases, or breaks a
limit is refused with a message that
names the problem, and a review-set without usable paths cannot be planned. A review that is made for a review-set
keeps its `id`, and the record digest that a signature covers includes it, so the next review of the same set can
start from the last signed one.

## Planning Reviews

If the repository has a `.reviewmark.yaml` with review-sets, you do not have to create each review by hand. On the
project page choose **Plan reviews** (it is shown when a project administrator has enabled a repository of the project;
otherwise the page says to ask one). The plan page at `/projects/{id}/plan` finds the review-sets that need a review and
creates the reviews you tick.

1. Choose the **repository** (only the enabled repositories of this project are offered; a single one is already
   chosen) and enter the **head revision**, a branch,
   a tag, or a commit, for example `main`.
1. Under **What to review** choose one of:
   - **Changes since the last signed review of each set** (the default): each set is compared with the head of its
     last signed review. A set that has never been signed is reviewed in full and says so in its note. If that
     earlier head is no longer an ancestor of the new head (for example after a force push), or the patterns of the set
     changed since then, the set is also reviewed in full.
   - **Changes since a branch, tag or commit**: a text box for the baseline revision appears. Every set is compared with
     that revision.
   - **All files of each set**: no baseline; every selected file counts as Added.
1. Choose **Show plan**. Nothing is created yet.

The plan starts with one sentence, for example "3 sets need review, 1 already filed, 9 have nothing to review.", a
muted line with the repository, the head, and the baseline that was used, and a sentence that says what to do next. Below
that:

- **To review**: the sets that need a review, each ticked, with its title, a note about the baseline, and how many
  files changed (added, modified, deleted). Clear the tick of the sets you do not want now. For a long list
  **Select all** and
  **Select none** appear. The button says how many reviews it creates, for example **Create 3 reviews**, and is
  disabled when nothing is ticked.
- **Cannot be planned**: shown only when a set cannot be planned, for example because its patterns are not valid,
  with the reason. Fix the set in `.reviewmark.yaml` and plan again.
- **Already filed**: shown only when a review of a set at this head and baseline exists, with a link to that review. A
  set is not offered twice, so you cannot file the same review again by mistake.
- **Nothing to review**: the sets whose files did not change. It is collapsed; open it to see which they are.

Choose **Create N reviews**. The page then lists one result per set: a link to the new review, or the reason it could not
be created. The others are still created. When a set fails because the repository could not be read, the reason is the
fixed sentence "The repository could not be read." A failed set is not created; choose **Plan again** to offer it once
more, or
go back to the project. A new review has the files of the plan, the checklist, and the set identifier, which the review
page shows as **Review set** in the Details. If you change the head revision, the baseline, or the repository, the plan
is cleared, because it no longer matches.

The creator of the reviews is you, as for any review. The reviews use the commits that the plan showed, so a commit pushed
while you read the plan does not change them.

When the plan cannot be made, the page says why in plain English and what to do:

- The project has no repository, or it was disabled: ask an administrator of the project.
- The revision is not known: check the spelling, and push the branch or tag if it is new.
- The head revision has no `.reviewmark.yaml`: add it, or choose another revision; you can still create a review by hand
  on the project page with **+ New review**.
- The configuration is not valid or too large: the message names the problem and the position; fix the file.
- The repository could not be read: try again later, or ask an administrator of the project.

The page never shows text printed by git, the location of the repository, or a credential.

### Limitations of Sign-in

- There is no password reset and no email. A system administrator sets a new password for a user who forgot it. A
  local account can turn on an optional second factor, a one-time code from an authenticator app (see Two-Factor
  Sign-in in the Users and Sign-in chapter); each account decides for itself, and this release does not enforce it by
  a policy, neither for system administrators nor for a project. Only one OpenID Connect provider can be set, an
  account at it is never linked to a local account, and it is not updated when the person changes at the provider (the
  name and the state of the account are those of the first sign-in, and a person who is removed at the provider keeps
  the account here until a system administrator disables it). The sign-in at a provider does not end the session at
  the provider when you log out.
- The lockout counts failures per user name, so anyone who knows a user name can keep that account locked for 5
  minutes at a time by guessing wrongly on purpose, also from an address that the throttle allows. The sign-in
  throttle (see Sign In and Out in the Users and Sign-in chapter) limits how often one address can try, but it does
  not prevent this. This is an
  accepted trade-off of locking by user name.
- Every failed sign-in gives the same answer, and an unknown user costs one password verification. The response time
  can still differ slightly between a known and an unknown user (a failure on a known account also stores the failure
  counter and an audit entry), so timing can in principle reveal which user names exist. This is not mitigated further.
- With `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` the server trusts `X-Forwarded-For` from any sender and does not
  restrict which proxies it trusts. The proxy must overwrite that header and be the only route to the server;
  otherwise a client can choose its own address and avoid the per-address and per-name limits (the lockout of the user
  name still applies).
- The sign-in throttle is kept in memory in one server process: it starts again when the server restarts, and with
  several server processes each has its own counters. It holds at most 10,000 counters and evicts the oldest when it
  is full, so a flood from very many addresses can push out older counters. Users behind one shared address (an office
  network or a proxy)
  share the limit of that address, and the address is only as reliable as the proxy setup (see Secure Cookie Behind a
  Reverse Proxy in the Deployment chapter). A wrong password at a signature is limited by the lockout of the user name
  only.
- The sign-in state of an open page is re-checked only every `Auth:RevalidationSeconds` seconds (30 by default), so a
  disabled or changed user can still use some actions of an open page for up to that long; see Session Ended on an
  Open Page in the Users and Sign-in chapter. Changing a password does not end the user's other sessions. Opening a
  page that needs a role from an
  already open page is checked against the role you had when the page was opened.

## Review Comments

Select a file on the review page to read it as numbered lines and to discuss it in comment threads. The line at the
top of the page says what is left to do: start a discussion, how many open threads are to be resolved, or that all
are resolved (it also counts the unanswered checks of the [Review Checklist](#review-checklist)). The file list shows
the number of open threads of each file.

The thread index lists the threads of all files with the file, the lines, the state, the number of comments, the
author of the first comment, and the time of the last activity, newest first. Use the All, Open, and Resolved
buttons (which show the totals) to filter it; it starts with Open. Press a row to show that thread: its file is
selected, its lines are highlighted, and the page scrolls to them.

![A side-by-side diff of a review file with one open and one resolved thread.](images/review-diff-thread.png)

1. To start a thread, either press the Comment button of a line, which fills in that line, or type a first and a last
   line number yourself (a first line without a last line means that one line), or leave both line fields empty to
   comment on the whole file. The form shows where the comment will go. Enter the comment text (1 to 4000
   characters) and press the start button. The author of the comment is you, the signed-in user.
2. A thread is listed below the file's code with the lines it covers (the whole file, a line, or a range of lines)
   and its comments in the order they were posted; the form to start a new thread follows it. Lines that have a
   thread are marked.
3. Any member of the project can reply to a thread. Press the resolve button to mark a thread resolved, and the reopen
   button to open it again. A resolved thread is shown as one line (lines, state, and number of comments); press its
   expand button to read it, reply, or reopen it, and the collapse button to fold it again.

Lines are numbered from 1. Windows (CRLF) and Unix (LF) line endings count the same, a line ending at the very end
of a file does not add a line, and an empty file has no lines. A binary file, or a file that is not valid UTF-8, and
an empty file can be commented on as a whole only. A line number must be a whole number inside the file, and the
first line must not be after the last line. Leading and trailing spaces of the text are removed. A
problem is shown as a message next to the form (or inside the thread for a reply) and nothing you typed is lost.

Posted comments cannot be edited or deleted, so the record of the discussion stays complete; only a thread's state
changes. While a review is approved it is locked and no thread can be started, answered, resolved, or reopened (see
[Locked Reviews and Reopening](#locked-reviews-and-reopening)). Comments posted by other people appear when you reload
the page or take an action on it. Comment text is
always shown as plain text; any markup in it is displayed as typed.

### Sharing links

Every file, line and thread of a review has a stable link that opens the review page at that place. The address bar
of your browser always holds the link of what you are looking at: choosing a file in the list, or a thread in the thread
index, changes the address without adding entries to the browser history, so the Back button still takes you to the
page you came from. Copy the address and send it to a colleague.

| Link | What it opens |
| ---- | ------------- |
| `/projects/{id}/reviews/{reviewId}` | The review (no file selected) |
| `/projects/{id}/reviews/{reviewId}/files/{path}` | That file, as a diff when it can be compared |
| `/projects/{id}/reviews/{reviewId}/files/{path}?line=12` | The same, scrolled to and highlighting head line 12 |
| `/projects/{id}/reviews/{reviewId}/files/{path}?line=12&side=baseline` | The same for baseline line 12 |
| `/projects/{id}/reviews/{reviewId}/threads/{threadId}` | The thread's file, scrolled to the highlighted thread |

Every thread has a **Link** next to its state, shown as a button; it opens the thread's address. Next to it,
**Copy link** copies the full address to the clipboard and briefly shows "Copied" next to the button (or "Copy failed"
when the browser does not allow it; the **Link** can still be copied with the context menu of your browser). Characters
in a file path that need escaping in an address
(spaces, `#`, `?`, `%`, and letters outside the English alphabet) are escaped for you in the links the page builds.

Reloading a link shows the same file, line or thread, always as the diff when the file has one (the choice between the
Diff, Head and Baseline views is not part of the link). If a link names something that does not exist in this review
(a file that is not in it, a thread of another review, a line the file does not have, or a baseline line of a file that
has no baseline) the page says so in plain words and offers a link back to the file list of the review; nothing else
is lost. If you are not signed in, opening a link takes you to the sign-in page, and signing in brings you back to the
same link. Only the user's own sign-in is needed: there is no separate access for a link, so a person who is not
signed in cannot read anything from it.

Known limits: after you choose a file or thread, the address bar shows its link, but a "Session ended" message that
appears later offers a sign-in link to the address the page was first opened at. A file whose name contains the text
`%2F` is found from its link only while that is the one file whose name matches.

## Review Checklist

Every new review gets a checklist: a copy of the checklist of its project. A project uses the default checklist,
taken from the Continuous Compliance review template (14 sections and 58 checks), until a project administrator
replaces it (see [Checklist Template](administration.md#checklist-template)). The review page shows a Checklist card
with a summary such as "37 of 58
checks answered" and a link to the checklist page; the review page's next-step line also counts the unanswered
checks (for example "12 checklist checks unanswered, 1 open thread to resolve.") and says "Checklist complete and
all threads resolved." when nothing is left.

The checklist page tells you what to do next at its top: how many checks are unanswered, the overall progress, and
a "Next unanswered check" button that opens the section of the first unanswered check and brings it into view.
Below that, every section is listed with its progress (for example "3 of 8"); only one section is open at a time,
and the first section with unanswered checks is open when you arrive.

![The checklist page: two sections done, one in progress, and a justified N/A answer.](images/checklist-partial.png)

1. Open a section and choose Pass, Fail, or N/A for each check. A Pass is saved at once. For Fail and N/A a
   justification (up to 2000 characters) is required: type it in the box that appears and press Save. A missing
   justification is reported next to the box, and nothing is saved until it is given. You can change an answer at any
   time until the review is approved; an approved review is locked and its checklist is read-only (see
   [Locked Reviews and Reopening](#locked-reviews-and-reopening)). Each answer is stored under your user, together
   with the UTC time, and shown as "Answered by" with your
   display name.
2. If a section does not apply to the review, clear its "This section applies to the review" switch, give the reason
   (required), and press Save reason. A section that does not apply counts as done and its checks leave the totals.
   Switch it on again to answer its checks; the reason is then cleared.

The checklist is complete when every check of every applicable section is answered. A Fail is a valid answer: it
counts as answered, and its justification records the finding. A review with a Fail cannot be signed, and the files
of a review never change, so the usual way on is a new review of the fixed revision (see When a Check Fails in the
Review Practices chapter). Any later change to the record, including a checklist answer of another reviewer, makes the
signatures given so far out of date, so answer first and sign last (see Sign Last in the same chapter).

The checklist of a review is fixed when the review is created: a later release with a different default checklist, or
a project checklist that is replaced afterwards, does not change reviews that already exist. Checklist
answers are not linked to comment threads. The checklist
page is made for narrow screens too.

## Review Signatures

A review is approved when its reviewers have signed it. The review page shows an **Approved** or **In review** badge
next to the review title, and a **Reviewers** card with the reviewer slots. The project page shows the same badge for
each review, next to a second badge with the state of the review (see the known limitation at the end of this
section). An Approved review is also locked (see [Locked Reviews and Reopening](#locked-reviews-and-reopening)).

**Reviewer slots.** A slot has a primary reviewer and, optionally, an alternate reviewer, chosen from the enabled
members of the project. Either of the two can sign the slot. A review can have any number of slots, or none; a review
without a slot stays In review and its next step says "Add a reviewer slot.". Any member of the project (and any
system administrator) can add a slot (open **Add a slot**,
choose the reviewers and press **Add slot**) and change or remove a slot that is not signed (open **Change
reviewers**, then **Save** or **Remove**). A user can be in only one slot of
a review, and the primary and the alternate must be different users. A slot that has been signed can no longer be
changed or removed, even if its signature is out of date; to change the reviewers, add a new slot. The slots of an
approved review cannot be changed at all (see [Locked Reviews and Reopening](#locked-reviews-and-reopening)).

**Independent reviewers.** In a project that requires independent reviewers (the default; see Review Rules in the
Administration chapter), the person who created the review cannot be the primary or the alternate reviewer of any
slot of it, and cannot sign it. Adding or changing a slot with the author is refused with "*Name* created this review
and cannot be one of its reviewers while independent reviewers are required.", and the review page leaves the author
out of the lists of people to choose from. The author is also told on the review page: "You created this review, so a
different project member has to review and sign it." A signature attempt by the author is refused with "You created
this review, so you cannot be a reviewer of it. A different project member must review and sign it, or a project
administrator can switch off 'Require independent reviewers'." This also holds for a slot that was filled while the
rule was switched off. The rule is not applied to signatures that already exist, and a review created from the
command line has no recorded author, so nobody is excluded from it. For a review created with an API token the
author is the owner of the token.

Known limitation: the author is the ReviewCenter user who created the review. ReviewCenter does not know who wrote
the reviewed files and does not match the authors of git commits, so a person who wrote the code but did not create
the review in ReviewCenter is not excluded. The rule helps to keep the creator of a review from approving it alone;
it is not proof that the reviewer did not write the code. A project in which one person has to do both can switch the
rule off.

**Signing.** The reviewer of a slot sees a signing form on the slot, if the review can be signed. Signing needs
membership of the project and being the assigned reviewer; a system administrator who is not a member cannot sign. The
form shows
the statement of the signature, "I have reviewed this record and approve it.", and asks you to confirm your password.
Enter your password and press **Sign**. A reviewer who has turned on the second factor also types a current code of
the authenticator app (see Two-Factor Sign-in in the Users and Sign-in chapter). The password is checked like at
sign-in, so
wrong passwords count toward the
same lockout, and it is never stored or shown. A reviewer whose account is at an identity provider has no password; the
slot offers **Confirm identity with** the provider instead, which sends the person to the provider to sign in again,
and after that the slot offers **Sign** (see
Sign In with an OpenID Connect Provider in the Users and Sign-in chapter). A review can be signed only when:

- it has a checklist, and every check of an applicable section is answered;
- no check of an applicable section is answered Fail (the files of a review never change, so after a Fail the usual
  way on is a new review of the fixed revision; see When a Check Fails in the Review Practices chapter); and
- no thread is open.

While one of these holds back the signature, the slot lists what is left instead of the form (for example "2 threads
are open"). Each signature records your name, your role in the slot (primary or alternate), the UTC time, and the
statement.

![The signing form of a reviewer slot with the statement and password confirmation.](images/signature-confirmation.png)

**What a signature covers.** A signature is bound to the record as it was when you signed. The record is the content
fingerprint of the files, the wording of every checklist question, every checklist answer with its justification and
the person who gave it, every section marked as not applicable with its reason, and every comment thread with its
comments. For a review from a repository it also covers the repository, the
baseline and head commits, the selection, and every changed file with its change kind and the hashes of its baseline
and head content, so a signature cannot look current for a different selection or a different change. The
**Record digest** in the Details section (shortened; the full
value is its tooltip) identifies that record. The time an answer was given is not part of it, but who gave it is: if
somebody else answers a check again, even with the same value, the earlier signatures go out of date. In general, any
change to the record, including another reviewer's checklist answer, a new comment, or a resolved thread, makes the
signatures given so far out of date. If the record
changes while you are signing, the signature is refused and you are asked to review it and try again.

**Out of date and Approved.** A slot shows "Signed by *name* on *date* UTC" while it has a signature of the current
record, "Signature out of date" when it has signatures but the record has changed since (for example a new comment or
a changed answer), and "Not signed" otherwise. A review is **Approved** when it has at least one slot and every slot is
signed for the current record. While some slots are still unsigned, a change to the record makes the signatures given
so far out of date, and the reviewers sign the new record again; the earlier signatures stay as history. Once the
review is Approved it is locked and cannot change any more, unless an administrator reopens it (see
[Locked Reviews and Reopening](#locked-reviews-and-reopening)); it then goes back to In review.

![The Reviewers and signing card of an approved review, with both slots signed.](images/review-signatures.png)

A user who is disabled while a page is open is signed out of that page at its next check (every 30 seconds by
default), and the page is replaced by the Session ended message. Until then the page can still accept actions that are
not re-checked, such as answering checks and adding comments and threads. Signing and changing reviewer slots check
the account again on every action and are refused for a disabled user at once.

Approved is computed each time from the record and the signatures; it is not a stored state of the review, and neither
is the lock that follows from it: a review is locked exactly while it is Approved. Every
signature is also recorded in the audit trail, which is described in Audit Trail in the Administration chapter. An
Approved
review can be downloaded as a digitally signed PDF; see the section Signed Attestation.

## Locked Reviews and Reopening

An approved review is **locked**. The lock is not a separate state: a review is locked while it has at least one
reviewer slot and every slot holds a signature of the current record, which is the same condition as **Approved**.
The review page then shows a **Locked** badge next to the Approved badge and a notice: "This review is approved and
locked. Its content, checklist and reviewers can no longer change. A project administrator can reopen it; the
signatures are then kept as history and the review must be signed again." The checklist page shows a similar notice.

**What is refused.** While a review is locked, ReviewCenter refuses every change of what was approved, whoever asks
and from wherever:

- starting a comment thread, replying to a thread, resolving a thread, and reopening a thread;
- answering a check, and switching a checklist section between applicable and not applicable (with its reason);
- adding, changing, and removing a reviewer slot; and
- signing (there is nothing left to sign).

The buttons and fields that do this are disabled on the review page, the checklist page and the line comment buttons
of the diff, with an explanation, and a request that gets through anyway (for example from a page that was opened
before the review was approved) is refused with the message "This review is approved and locked. A project
administrator must reopen it before it can change." Signing an approved review is refused with the same message. The
files and the selection of a review are fixed when it is created, so there is nothing to lock there. Everything that
only reads keeps working on a locked review: reading the files, the diff, the threads and the checklist, copying
links, the signed attestation PDF, and the evidence index. Project settings and project members are not part of a
review and are not locked.

A review in which only some of the slots are signed is **not** locked: it can still change, and a change makes the
signatures that were given out of date (see [Review Signatures](#review-signatures)). The lock begins with the last
signature.

**Reopening.** The only way out of the lock is an explicit, recorded action. A project administrator or a system
administrator opens the locked review and, in the locked notice at the top of the page, enters a **Reason for
reopening** (required, 1 to 250 characters) and presses **Reopen review**. Other users do not see the form. An API
token cannot reopen a review: it is a step of the web pages, and a request with a token is refused. A reopen of a
review that is not locked is refused with "The review is not locked, so there is nothing to reopen.", and an empty or
too long reason with "A reason of at most 250 characters is required to reopen a review.".

**What a reopen does.**

- The review is In review again and can change: threads, checklist and reviewers work as before.
- The signatures that were given stay in the record as history, but they are **out of date** for good. A reopen
  changes the record digest (the number of reopens is part of it), so no earlier signature can become current again,
  not even when the content is changed back to what it was. The slots show "Signature out of date". The reviewers
  have to review the review again and sign again; the people of the slots stay, and the checklist and the threads must
  be complete as for any signature.
- The reopen is kept with the review as history, listed on the review page in a **Reopen history** card (who, when in
  UTC, the reason, and the shortened digest of the record that was reopened), oldest first.
- The audit trail gets an entry `review.reopened` with the administrator as the actor and the detail
  `previous digest <digest>; reason: <reason>`. The change and its audit entry are stored together.
- The attestation PDF of a review that was reopened and approved again has a **Reopen history** section. It lists each
  reopen with the time, the person who reopened, the reason and the previous digest. A review that was never
  reopened has no such section. The name of the person who reopened follows the reviewer names setting of the project
  (see Evidence Settings in the Administration chapter).

The review can be approved and locked again by new signatures, and reopened again; each reopen is one more entry of
the history. A review that was reopened is not in the evidence index until it is approved again.

**Known limitation: a Draft badge next to an Approved badge.** Every review row of the project page shows two badges:
the state of the review and the approval. The state is stored with the review, and `Draft` is the only state in this
release, so every row shows **Draft**, also the row of a review that is **Approved**. A review can therefore appear as
"Draft" and "Approved" at once. The two badges mean different things: **Draft** is the stored state, which no action
changes yet, and **Approved** or **In review** is the signature status that is computed from the record. The review
page itself shows only the Approved or In review badge in its header; the state `Draft` is listed in its Details
section. Read **Approved** as the status of the signatures. A later release is planned to add more review states.

## Signed Attestation

When a review is **Approved**, its page shows **Download attestation (PDF)** on a line of its own below the title, with
a note about the key that signs the file. The PDF is made when you press the button and is not stored. It contains the
review (project, review, repository and commits, the content fingerprint and the hash of every file), the checklist
with every answer and justification in full (a comment is never shortened), the comment threads, the sign-off of every
reviewer slot with the record digest it was bound to, and the state of the audit trail (the number of entries, whether
the chain is intact, and the head hash). The counts of open and resolved threads are exact. The first 100 resolved
threads are listed, each with the start of its first comment (at most 300 characters), and a line says how many more
there are; the full threads are in the review record. A review that was reopened by an administrator also has a
**Reopen history** section (see [Locked Reviews and Reopening](#locked-reviews-and-reopening)). The last section
describes the signing key and what the
signature proves. A review that is not Approved has no button; a direct request to its `attestation.pdf` address is
refused with the message "The review is not complete" (status 409). For a caller who is not signed in (the anonymous API
route, see Evidence Settings in the Administration chapter) a review that is not complete is answered 404, like a
review that does not exist, so that review identifiers cannot be probed; signed-in members still get the 409. If the
review changes
while the PDF is being
prepared (for example an administrator reopens the review), nothing is signed or recorded and the request is also refused
with status 409 ("The review changed while the attestation was being prepared; try again."); press the button again.
Every download is recorded in the audit trail as `attestation.generated`. Only members of the project and system
administrators can download the PDF; for anyone else the address answers 404, as for a project that does not exist. The
one exception is the API route of the PDF in a project whose administrators turned on **Anonymous attestation PDFs**
(see Evidence Settings in the Administration chapter): anyone can then download it without signing in, and the
download is
recorded as `attestation.generated` by `anonymous`.

![An approved review with the Download attestation (PDF) button and the signing key note.](images/review-signed.png)

**What the PDF contains follows the evidence settings of the project.** Every attestation PDF of a project, whoever
downloads it, honors the five content switches of the project: the review discussion threads, the reviewer names, the
checklist wording, the file list with hashes, and the git context. A section that a switch turns off is left out, and
the names are replaced by neutral labels such as *Reviewer 1 (primary)* and *Participant 1*. A PDF **always** has a
**Content scope** section, right after the review section, that states which sections were left out and the settings
that were in effect when the PDF was made, so a reader always knows what the PDF does not show. These parts are always
included, whatever the settings: the review and project identifiers, the content fingerprint, the checklist outcomes,
the sign-offs, the record digest, and the note about the signing key. The record digest always covers the full record,
including what was left out of the PDF, and the digital signature is made after the content is chosen, so it is valid
for the reduced PDF too. Names that someone typed into a comment or another free-text field are not removed. Leaving
content out can make the document unsuitable for your regulatory regime; see Evidence Settings in the Administration
chapter.

### What a Verifier Sees

Open the PDF in a PDF reader that checks signatures, for example Adobe Acrobat. The signature panel shows the
certificate (subject, issuer, and thumbprint, which are also printed on the last page of the PDF) and whether the file
has changed since it was signed.

- **Proves**: the PDF has not been changed since it was signed, and it was signed with the key of the certificate that
  is named in it. The signature is a certifying signature that allows no changes, and Adobe Acrobat says so in the
  signature properties.
- **Does not prove who the reviewers are.** The reviewers are identified by the signatures in the review record, which
  ReviewCenter made after asking each reviewer for the password again. The PDF lists them, and the signature vouches
  for that list only as far as the key of the server is trusted.
- **Does not prove when it was signed.** The signing time is the clock of the server. This release uses no time stamp
  authority, so a reader can only say that the time is the one the signer claims.
- **Does not make the review correct.** It shows what the record said at the moment the PDF was made.

A reader reports the signer as **not trusted** unless it trusts the certificate. For a self-signed key it always does;
for an organization certificate it does once the root certificate of the issuing authority is installed on the reader's
machine.

### The Signing Key

The server signs with one key, which an administrator chooses in the `Attestation` section of `appsettings.json` or
with environment variables (a double underscore stands for the colon, for example `Attestation__Provider`):

`Provider` is `SelfSigned` or `CertificateStore`; the settings marked "Store" apply to `CertificateStore` only.

| Setting                             | Environment variable                 | Meaning                               |
| ----------------------------------- | ------------------------------------ | ------------------------------------- |
| `Attestation:Provider`              | `Attestation__Provider`              | Key source; default `SelfSigned`      |
| `Attestation:RequireTrustedKey`     | `Attestation__RequireTrustedKey`     | Refuse unsuitable keys; default false |
| `Attestation:CertificateThumbprint` | `Attestation__CertificateThumbprint` | Store: certificate thumbprint         |
| `Attestation:StoreLocation`         | `Attestation__StoreLocation`         | Store: `CurrentUser` or `LocalMachine`|
| `Attestation:StoreName`             | `Attestation__StoreName`             | Store: store name; default `My`       |
| `Attestation:SelfSignedFolder`      | `Attestation__SelfSignedFolder`      | Folder of the self-signed key file    |

**The self-signed key (default) is for development and test only.** On first use the server creates a key and a
self-signed certificate named "ReviewCenter self-signed attestation key (not an organization key)", and keeps them in
`attestation-selfsigned/attestation-selfsigned.pfx` in the folder of the Data Protection keys (beside the database, see
Repositories and Credentials in the Administration chapter), unless `Attestation:SelfSignedFolder` names another
folder. The file has no password, so protect the folder and back it up together with the keys; whoever can read the file
can sign as the server. The review page and the PDF say that the key is self-signed and not an organization key. If an
administrator supplies a self-signed certificate through the certificate store, the review page says that it is a
self-signed certificate supplied by the administrator from the certificate store, not an organization key and not
issued by a certificate authority; it says "generated by this server" only for the key that the server creates itself.

**Choose the certificate store for real use.** Set `Attestation:Provider` to `CertificateStore` and
`Attestation:CertificateThumbprint` to the thumbprint of the certificate.

- *Choose a document-signing certificate.* Its key usage must allow digital signatures or non-repudiation. A
  certificate that allows code signing only is not suited to signing a PDF. The server still uses it (the code-signing
  usage is not a reason to refuse a key, also not with `RequireTrustedKey`; the other rules of that setting still
  apply), logs a warning when it finds one at start-up, and prints one line in the
  signing-key section of the PDF: "This certificate is a code-signing certificate. Adobe Acrobat will not trust it for
  document signatures; use a document-signing certificate for production." A certificate without an extended key usage
  is not treated as code signing. The server does not check the certificate chain or revocation for any key; the PDF
  viewer does. A certificate from an internal certificate authority is fine, provided the readers of the PDF trust that
  authority: give them its root certificate.
- *Find the thumbprint.* In PowerShell run `Get-ChildItem Cert:\CurrentUser\My` (or `Cert:\LocalMachine\My`) and copy
  the thumbprint of the certificate, or open the certificate (`certmgr.msc` for the current user, `certlm.msc` for the
  machine) and copy the **Thumbprint** from the **Details** tab. Spaces are ignored.
- *Give the server access to the private key.* The key is used where it is and is never exported. The account that runs
  the server must be allowed to use it: for a certificate in `LocalMachine\My`, grant that account read access to the
  private key (Manage Private Keys in `certlm.msc`); for `CurrentUser\My`, the server must run as the user who owns the
  certificate. A key on a smart card or in a hardware module can be used when it is present.
- *Set `RequireTrustedKey` to `true`* to make the server refuse a self-signed key, a certificate whose key usage does
  not allow digital signatures or non-repudiation, and a certificate that is not valid now. This is all it checks: it
  does **not** check that the
  certificate chains to a trusted root and does **not** check revocation.

The server checks the key when it starts. A setting that cannot work (an unknown provider, a missing thumbprint, a
certificate that is not in the store, or `RequireTrustedKey` with a self-signed key or a certificate without a signing
key usage) stops the start with a message
that names the setting. If the key later becomes unusable (for example the certificate expires while the server runs),
the page shows the problem instead of the button, and a direct request is answered with status 500 ("Attestation
signing key unavailable"). A signed-out request to the address is sent to the sign-in page.

### Limitations of the Attestation

- The default key is self-signed and is not an organization key.
- There is no time stamp authority; the signing time is the server clock.
- Signing with a key in Azure Key Vault and with an RFC 3161 time stamp authority are planned, not implemented.
- The server does not check the trust chain or the revocation of the certificate; the reader does.
- The PDF uses the standard PDF fonts (Windows-1252 characters), so any other character is shown as a question mark.
- What the PDF contains follows the evidence settings of the project (see
  Evidence Settings in the Administration chapter); a PDF made with content switches off is a reduced document, and its
  **Content scope** section says so.
