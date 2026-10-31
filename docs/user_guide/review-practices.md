# Review Practices

This chapter gives suggested practices for running a review in ReviewCenter. Each practice says why it helps. What the
product enforces is described in *What the Product Enforces* below and in the *Reviewing* and *Administration* chapters;
everything else in this chapter is guidance or team convention that the product neither supports nor checks. Adapt it
to your own process.

## What the Product Enforces

Know the hard rules first, because the practices build on them. A review can be signed only when:

- every check of an applicable checklist section is answered;
- no check is answered Fail; and
- no comment thread is open.

Signing asks the signer to confirm their identity again (the password, or a fresh sign-in at the identity provider),
and a signature covers the whole record as it was at that moment. While only some slots are signed, any change to the
record, such as a new comment or a changed answer, makes the signatures given so far out of date; the page warns you
and asks for a confirmation first (see *What Makes a Signature Out of Date*). When the last slot is
signed, the review is Approved and **locked**: ReviewCenter then refuses every change of its threads, checklist, and
reviewers until a project administrator reopens it (see *Approved Reviews Are Locked* below).

The product also constrains the reviewers: a slot holds only enabled members of the project, a person can be in only
one slot, and only the primary or the alternate of a slot can sign it. In addition, a project requires independent
reviewers by default: the person who created the review in ReviewCenter cannot be a reviewer of it or sign it. That
setting can be switched off per project, it compares only the creator of the review (not the authors of git commits),
and it does not apply to a review created from the command line, which has no recorded author. Two more rules are on by
default: the person who created a review cannot answer its checklist, and the person who reopened an approved review
cannot sign it again (see *Who May Answer the Checklist* and *Approved Reviews Are Locked*). See the Reviewing and
Administration chapters (Review Rules) for the exact rules. Everything else is up to the team: who writes a comment,
who resolves it, and how it is worded.

> **These rules apply to all existing projects.** The rules are on by default and apply to ALL existing projects, also
> those created before this version (the stored columns default to on). A project that needs one person to do
> everything switches them off on the project page.

## Who May Answer the Checklist

While the project requires it (the default), the person who created the review in ReviewCenter cannot answer its
checks or change which sections apply; another member of the project has to. The checklist page tells the author so and
disables the controls. The author can still start, answer, and resolve comment threads, because the discussion is
where the author explains the change. Plan for it: create the review, then ask a second member to go through the
checklist, or let a reviewer do it. A project where one person has to do everything switches the rule off on the
project page; every change of the rule is audited.

## What Makes a Signature Out of Date

A signature stays current only while the record is the one that was signed. These changes alter the record, so each one
makes the current signatures out of date: a new comment thread, a reply, resolving or reopening a thread, a changed
checklist answer (also the justification, or who gave the answer), and switching a checklist section to not applicable
or back. A save that changes nothing, such as the same person saving the same answer again, does not. The due date of
a review is planning information and is not part of the record, so changing it leaves the signatures current.

ReviewCenter does not let such a change go through silently. When the change would make a current signature out of
date, the page stores nothing and shows a warning where you worked. It names every affected slot and its signer, for
example "Slot 1 (Ann)". **Confirm change** makes the change and the named signatures go out of date; **Cancel** removes
the warning and changes nothing, and what you typed stays in its field so that you can still use it. If another
reviewer signs while the warning is open, the first **Confirm change** is answered with a new warning that names every
signature, so you only confirm what you have seen. The signers then sign the new record again.

The warning is the moment to ask whether the change is needed now. Most of the time it is better to cancel, wait until
the signers are done, or agree with them first (see *Sign Last* below).

## Answering the Checklist

- **Pass** when you looked at the files of the review and the check is met. A Pass needs no text and is saved at once,
  so do not use it to move on quickly; an answer is recorded under your name and goes into the signed record.
- **Fail** when the check is not met. A justification is required. State what is wrong and where, and point to the
  thread that holds the details, because checklist answers are not linked to threads by the product.
- **N/A** when the check cannot apply to the files of this review. A justification is required as well. Say why in a
  way a later reader can verify, for example "No database code in this change set", not just "not relevant". If a whole
  section does not apply, clear the switch of the section and give the reason once.

Do not answer N/A to avoid a Fail. If the check should hold and does not, it is a Fail.

## Sign Last

Any change to the record, including a checklist answer of another reviewer (even an identical one), a new comment, or
a resolved thread, makes the signatures given so far out of date. So agree who is still to answer or comment, let them
finish, and only then sign; the signature of the last reviewer is the one that approves the review. If a late change
is needed, the page warns you which signatures it voids; after you confirm, the reviewers who already signed sign the
new record again.

## When a Check Fails

A Fail is a valid, recorded answer, but a review with a Fail cannot be signed. The product has no signed "review
failed" or "rejected" outcome yet: a review is either Approved or still In review, and the evidence index lists only
complete reviews. A suggested path:

1. Record the Fail with a justification, and start a thread on the lines concerned that explains why and what the
   author should change.
2. The author fixes the code in the repository. The files of an existing review never change, so the fix needs a new
   review of the fixed revision.
3. Create a follow-up review of the fixed revision: choose the failed review under **Follow up on an earlier review**
   in the form for a new review. The new review takes over what still holds and shows the Fail again, so the
   reviewers decide it anew (see Follow-up Reviews in the Reviewing chapter).
4. Ask the reviewers to check that the earlier findings are fixed, resolve the carried threads, answer the failed
   check again, and sign.

The earlier review stays In review. Teams that need evidence of a failed review should keep that unsigned review, with
its threads, answers, and its **History** (the audit trail of the review); the follow-up names it, and its attestation
says which review it follows.
There is no attestation PDF for a review that is not Approved, so the review record and its history are the evidence.

## Handling Minor Findings Without Blocking

Every open thread blocks signing, so a nit-pick must be closed explicitly. Three ways keep the review moving and keep a
record of the decision:

- **Fix and resolve.** The author changes the code in a later revision, replies "Fixed in the next review", and
  resolves the thread.
- **Accept or decline.** Reply with a plain sentence such as "Accepted as is: the name matches the existing API" or
  "Will not fix: the table is generated", and resolve the thread.
- **Track it elsewhere.** Collect several minor findings in one ticket in your own tracker, reply "Tracked in PROJ-123",
  and resolve the threads. ReviewCenter has no link to a tracker, so the reference is plain text.

Resolution has no text of its own: the **Resolve** button only changes the state of the thread. Write the reason as a
reply first, then resolve. A reply can also be added to a resolved thread (expand it first), and it does not reopen it.
Remember that a reply or a resolve after some reviewers have signed makes their signatures out of date, and after the
last signature the review is locked and refuses both, so close every thread before the first signature.
Say what was done and who decided, for example "Declined by the author after discussion with Alex: ...", because a
resolved thread is shown collapsed, and the audit trail records who pressed the button but not why.

## Writing Good Threads

- **Anchor to the lines.** Start the thread on the exact line or range, not on the whole file, so the author sees what
  you mean in the diff and the link of the thread opens the right place.
- **One concern per thread.** A thread is resolved as a whole. Two concerns in one thread make it hard to resolve one
  and keep the other.
- **State the expected outcome.** Say what would resolve it: "Rename to match X", "Add a test for the empty case", or
  "Explain why this is safe".
- **Name the severity in words.** The product has no severity flag, so start the first comment with a word that your
  team agrees on, such as "Blocking:", "Nit:", or "Question:". It tells the author what needs a fix before the review
  can be signed and what can be answered or tracked.

Posted comments cannot be edited or deleted. Think before you post, and correct yourself with a reply.

## Who Resolves a Thread

Prefer that the person who raised the thread resolves it, after the author has replied. If the author and the reviewer
agree on an outcome, the reviewer who agrees with it can resolve it instead. Do not let the author resolve their own
reviewers' threads without a reply that the reviewer has seen. The product does not enforce any of this: every member of
the project can resolve or reopen any thread, so it is a convention to agree on in the team. Reopen a thread that was
resolved too early, before the review is approved; after that the lock refuses it.

## Baselines and Scope

- **Keep reviews small.** A review of a few related files can be read carefully, and a Fail or an open thread blocks
  only that review.
- **Review against the previous revision.** Give a repository review a baseline, so the diff shows what changed and the
  reviewers do not read the unchanged code again. With **Plan reviews**, the default compares each review-set with its
  last signed review, so changes of an unsigned (for example failed) review are not skipped.
- **Know the trade-off with ReviewMark.** A review made from the changes since a baseline holds only the changed
  files, so its fingerprint is not the one ReviewMark computes for the whole review-set, and ReviewMark reports it as
  stale. If you enforce review-sets with the evidence index, review all files of the set (see Use the Evidence Index
  with ReviewMark in the Automation and API Tokens chapter).

## Alternates

A slot has a primary reviewer and, optionally, an alternate, and either one can sign the slot. Add an alternate when
the primary may be away, so that a signature does not wait for one person. Choose someone with the same competence,
because the signature records the role (primary or alternate) of the person who signed, and the alternate carries the
same responsibility. Agree in the team who signs when both are available; usually it is the primary. A person can be in
only one slot of a review, and a signed slot cannot be changed, so decide the reviewers before the first signature.
Independence from the author is enforced by default, but only as described under *What the Product Enforces*: it covers
the creator of the review in ReviewCenter, a project administrator can switch it off, and it does not know who wrote
the code. If your process needs more (for example, that nobody who committed to the change reviews it), that is a
convention: check the commit authors yourself when you choose the reviewers. Keep your team's rule consistent with the
project's review rules setting.

## Approved Reviews Are Locked

An Approved review is **Locked**: every slot has signed the current record, and the review page shows a Locked badge.
The lock is not a separate state; it lasts exactly while the review is Approved. A project administrator or a system
administrator can **Reopen** it in the locked notice of the review page, with a reason of 1 to 250 characters. A reopen
is audited (`review.reopened`, with the reason and the previous digest) and listed in the **Reopen history** of the
review. It puts the review back In review. The earlier signatures stay as history but are out of date for good, even
if the content is changed back, so the reviewers must sign again. Reading, the attestation PDF, and the evidence index
keep working while the review is locked, and a review that was reopened is not in the evidence index until it is
approved again.

- **Treat Approved as final.** Finish the discussion, the checklist, and the reviewer slots before the last signature,
  because afterward nothing can change without a reopen.
- **Reopen sparingly.** A reopen undoes the approval and nothing takes it back. Reopen only when the approval must
  really be undone, for example a wrong answer or a finding that was missed, not for a late nit-pick or a typo.
- **State the reason concretely.** The reason is recorded as text in the audit trail and, once the review is approved
  again, in the attestation PDF, so write what was wrong and who asked, for example "Missed SQL injection in Orders.cs
  line 40, found by Alex after approval", not "needs rework".
- **Prefer a new review for new work.** Files of a review never change. If the code changed after approval, create a
  new review of the new revision (see *When a Check Fails*) and leave the approved one as the record of what was
  approved. Reopen is for correcting the judgment on the same files.
- **Sign again with care.** After a reopen, the reviewers read what changed (the **Reopen history** and the **History**
  of the review) before they sign the new record.
- **Reopen is not a re-stamp.** While the project requires it (the default), the person who reopened a review cannot
  sign it again, so the one who undoes an approval cannot approve the new record alone. The review page shows that
  person a notice instead of the sign form. A reopen is refused when it would leave a slot in which nobody may sign (for
  example, the administrator is the only reviewer of a slot), and the message names the slots. Give each slot an
  alternate, or ask another administrator to reopen, or switch the rule off for the project.

## Signing Hygiene

- **Sign only when the record you saw is final.** Before you sign, check that all threads and answers are as you
  expect. While other slots are unsigned, anything that changes afterward makes your signature out of date; when you
  are the last signer, the review locks and the record cannot change without an administrator's reopen.
- **A stale signature means review again.** When a slot says "Signature out of date", read what changed (the thread
  list and the **History** of the review show it) before you sign the new record. Do not sign only because the
  button is there.
- **Never share an account.** The signature is bound to the person who confirmed their identity, and the audit trail
  names that person. A shared account or a colleague typing your password makes the record untrue.
- **Use the second factor** for reviewer accounts where your policy allows it, since it makes the confirmation at
  signing stronger.

## Project Settings

Project administrators decide what the attestation PDFs contain and whether evidence can be read without signing in.
Check those settings before evidence is shared or downloaded: an attestation PDF is generated on each download and is
not stored, so the settings in effect at that moment decide what it contains. See Evidence Settings in the
Administration chapter.
