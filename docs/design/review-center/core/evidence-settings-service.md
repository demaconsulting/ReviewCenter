### EvidenceSettingsService

![Core Structure](CoreView.svg)

#### Purpose

`EvidenceSettingsService` owns the rules for the evidence settings of a project: who may read and change them, how a
change is made safely, and how it is recorded. The settings decide who may read the evidence of the project without
signing in (the evidence index and the attestation PDFs) and which sections the attestation PDFs contain. The unit is
also the home of the types that carry the settings, `EvidenceSettings` and `EvidenceSettingChange`. Who may see or
administer a project is decided by `ProjectAccess`; this unit only asks.

#### Data Model

**EvidenceSettings**: an immutable record of seven booleans. `AnonymousIndex` (anyone may read the evidence index of
the project without signing in) and `AnonymousAttestations` (anyone may download its attestation PDFs without signing
in) default to off, so evidence is private unless a project administrator opens it. `IncludeThreads` (the review
discussion threads and their comments), `IncludeReviewerNames` (the names of the people), `IncludeChecklistWording`
(the wording of the checklist questions and section titles), `IncludeFiles` (the list of files with their hashes), and
`IncludeGitContext` (the repository name and the baseline and head revisions) default to on, so a PDF is complete unless
a project administrator reduces it. `EvidenceSettings.Default` holds these defaults. Whatever the switches say, a PDF
always carries the review and project identifiers, the content fingerprint, the checklist outcomes, the signatures, the
record digest, and the signing-key note, and the record digest always covers the complete record.

- **OmittedSections**: the names of the content sections the settings leave out, in the fixed order threads, reviewer
  names, checklist wording, file list, git context, in the words "Review discussion threads", "Reviewer names",
  "Checklist wording", "File list and hashes", and "Git context". The settings page, the content scope section of every
  PDF, and the audit trail use the same words, so the three can be compared. `IsReduced` is true when the list is not
  empty. The two anonymous switches are not a reduction of the content.
- **ChangesFrom(earlier)**: the `EvidenceSettingChange` values (`Key`, `Old`, `New`) of the settings that differ from an
  earlier state, in the order of the properties, empty when they are equal. The key is the name of the property.

**Project.Evidence**: the settings of a project are the property `Evidence` of the `Project` type (`EvidenceSettings`,
`EvidenceSettings.Default` when none were set). The store keeps them as seven columns of the project.

**IReviewStore.UpdateEvidenceSettingsAsync**: the one store operation of the unit. It takes the project identity, the
expected settings, the new settings, and the audit draft, and changes the settings only if the stored settings still
equal the expected ones, together with the audit entry in one transaction. It returns `false`, with nothing changed and
nothing recorded, when the project does not exist or the stored settings are no longer the expected ones. It is
implemented by `ReviewStore` in the `Storage` subsystem.

**Constants**: `ChangedAction` (`project.evidence-settings-changed`) and `ChangedByOthersMessage` (the text of the
conflict: the settings were changed by someone else, the current settings are shown, check them and save again).

The service holds the injected store and access rules and no other state, so it is safe for concurrent use when they
are.

#### Key Methods

**GetAsync**: Reads the settings of a project for someone who administers it.

- *Parameters*: the `Actor` (may be null), the project identity, and a cancellation token.
- *Returns*: `Task<EvidenceSettings>` — the settings of the project.
- *Preconditions*: The actor is a system administrator or a project administrator of the project
  (`IProjectAccess.RequireAdministerAsync`). A project the actor may not see, a project that does not exist, a user
  who is disabled, an unknown user, and a missing actor give `ProjectNotFound`; a plain member gives
  `ActorNotAllowed`.
- *Postconditions*: Nothing is changed.

**GetEffectiveAsync**: Reads the settings of a project without regard to who asks, for the services and the API that
must apply the settings to a request (whether anonymous access is allowed, what a PDF contains). It returns the
settings, or null when the project does not exist. It takes no actor and grants nothing by itself: a caller that uses
it for a decision about access applies the rules of its own kind of request.

**SetAsync**: Changes the settings of a project.

- *Parameters*: the `Actor`, the project identity, the `expected` settings (what the caller read and based its change
  on), the `updated` settings, and a cancellation token.
- *Returns*: `Task<bool>` — `true` when the settings were changed, `false` when `updated` equals `expected`.
- *Preconditions*: `expected` and `updated` are not null (`ArgumentNullException`); the actor passes the same check as
  for `GetAsync`, made again at every call, so a user who was demoted or disabled while a page was open gets nothing
  through; a missing actor is refused with `ActorNotAllowed` even if an implementation of the access rules would let
  it pass.
- *Postconditions*: When `updated` differs from `expected`, the new settings are stored for that project only, with one
  audit entry; when the stored settings are no longer `expected` (someone else changed them first), nothing is stored
  and `RecordChanged` is raised.

The service first compares `updated` with `expected` (`ChangesFrom`). With no difference it returns `false` without
touching the store and records nothing, so a save that changes nothing leaves no entry. Otherwise it builds the audit
draft and calls `UpdateEvidenceSettingsAsync`, whose compare-and-set is atomic with the update; when the store reports
`false` the service raises `RecordChanged` with `ChangedByOthersMessage`. A caller reports that rule as a conflict (the
API as HTTP 409) and shows the current settings.

**Audit**: The change is recorded as the single event `project.evidence-settings-changed`, built from the actor, the
project identity (the entry carries the project and no review), and the detail, which lists each changed key with its
old and new value in the order of the properties, separated by a semicolon and a space, with `on` and `off` for the
values, for example
`AnonymousAttestations off->on; IncludeThreads on->off`. The draft is passed to the store, which saves it in the same
transaction as the change, so a change without its entry, or an entry without its change, cannot exist. A refused change
records nothing. See the `AuditService` unit for the action codes.

#### Error Handling

Rule violations raise `DomainRuleException` before any store change: `ProjectNotFound` (the project does not exist or
the actor may not see it, with the same message for both), `ActorNotAllowed` (a plain member, or no actor), and
`RecordChanged` (the settings were changed meanwhile). A project that does not exist is also `ProjectNotFound` for
`SetAsync` and `GetAsync`, and null for `GetEffectiveAsync`. Constructing the service with a null store or null access
rules, and calling `SetAsync` with null settings, raise `ArgumentNullException`. Store failures propagate unchanged.

#### Known Limitations

- The right to change the settings is checked before the store transaction and not inside it. An administrator whose
  right is revoked within the few milliseconds between the check and the transaction of one call is not stopped for that
  one change; the next call is refused. This is the same class of limitation as for the member operations of
  `ProjectService`, is deliberate, and is not mitigated.
- The storage has no migration (see `ReviewCenterDbContext`): a development database that was created before the
  settings existed does not have the seven columns and must be recreated.

#### Dependencies

- **IReviewStore** — reads projects (with their settings) and changes the settings with the audit entry (injected).
- **IProjectAccess** — decides who may administer the project (injected).
- **AuditDraft** and **Actor** — the audit entry and the person who made the change.

#### Callers

- **EvidenceSettingsPage** — reads the settings for the administrators of a project and saves a change.
- **EvidenceIndexService** and **AnonymousEvidenceHandler** (in the `Api` subsystem) — read the effective settings to
  decide what an evidence request may see; the attestation service applies the settings of the project to the content of
  a PDF.
- **ServerHost** — registers the service as a single instance.
