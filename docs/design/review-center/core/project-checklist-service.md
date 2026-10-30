### ProjectChecklistService

![Core Structure](CoreView.svg)

#### Purpose

`ProjectChecklistService` owns the rules for the checklist that a project gives to its new reviews: who may read and
change it, which definitions are accepted, how a change is made safely, and how it is recorded. A project without a
definition of its own uses the built-in default checklist (`DefaultChecklist.Current`); a project administrator can
replace it with a definition of their own, or go back to the default. The wording is part of what every signature
covers, so a change is strict. The unit also owns the `ProjectChecklist` type that describes the checklist of a project
for a caller. The validation itself is the `ChecklistValidator` class of the `ChecklistService` unit, and the copy that
a review takes of the definition is made by `ReviewService`; this unit only decides what may be stored. Who may see or
administer a project is decided by `ProjectAccess`; this unit only asks.

#### Data Model

**Project.ChecklistDefinition**: the definition of a project is the nullable text property `ChecklistDefinition` of the
`Project` type, JSON in the form of `ChecklistDefinition.ToJson`. `null` stands for the built-in default. The version
label is part of the JSON; there is no separate version property, so the label and the wording cannot disagree. The
store keeps the text in one unbounded text column of the project.

**ProjectChecklist**: an immutable record of what a caller needs to show or change the checklist: the parsed
`Definition`, `IsDefault` (true when the project has no definition of its own), the `Json` text (of the default's
`ToJson` when `IsDefault`), and the `Hash`. `Version`, `SectionCount`, and `ItemCount` are derived from the definition.

**Hash**: `HashOf(storedJson)` is the lower-case hexadecimal SHA-256 of the UTF-8 bytes of the stored text, and of the
text of the default for `null`, so that "never changed" is a value like any other. A caller reads the checklist, keeps
the `Hash`, and hands it back as `expectedHash` with its change. The service and the store use the one definition.

**IReviewStore.UpdateChecklistDefinitionAsync**: the one store operation of the unit. It takes the project identity, the
expected hash, the new text (`null` removes the definition), and the audit draft, and changes the definition only if the
hash of the stored text still equals the expected hash, together with the audit entry in one transaction. It returns
`false`, with nothing changed and nothing recorded, when the project does not exist or the stored definition is no longer
the expected one. It is implemented by `ReviewStore` in the `Storage` subsystem.

**Constants**: `ChangedAction` (`project.checklist-changed`) and `ChangedByOthersMessage` (the text of the conflict: the
checklist was changed by someone else, the current checklist is shown, check it and save again). The limits of a
definition are in `ReviewLimits` (`MaxChecklistBytes` of 256 KiB, `MaxChecklistSections` of 50, `MaxChecklistItems` of
500, and the lengths of the version, identifiers, titles, wording, and hint).

The service holds the injected store and access rules and no other state, so it is safe for concurrent use when they
are.

#### Key Methods

**HashOf**: Static. Gives the hash described above for a stored text or `null`.

**Validate**: Static and pure. Checks the text of a definition with `ChecklistValidator` and returns every error with its
path, or the parsed definition. Nothing is stored or recorded. It does not apply the version-label rule, which needs the
stored definition.

**GetAsync**: Reads the checklist a project gives to its new reviews.

- *Parameters*: the `Actor` (may be null), the project identity, and a cancellation token.
- *Returns*: `Task<ProjectChecklist>` — the project's own definition, or the default.
- *Preconditions*: The actor may see the project (`IProjectAccess.RequireViewAsync`). A project the actor may not see, a
  project that does not exist, a disabled or unknown user, and a missing actor give `ProjectNotFound`.
- *Postconditions*: Nothing is changed. A stored text that cannot be read gives `ChecklistDefinitionInvalid`; it is not
  replaced by the default.

**SetAsync**: Replaces the definition of a project.

- *Parameters*: the `Actor`, the project identity, the `expectedHash`, the new JSON text, and a cancellation token.
- *Returns*: `Task<bool>` — `true` when the definition was changed, `false` when the new definition has the same content
  as the current one.
- *Preconditions*: the actor and `expectedHash` are not null (`ArgumentNullException`); the actor is a person, not an API
  token (`ActorNotAllowed`); the actor is a system administrator or a project administrator of the project
  (`IProjectAccess.RequireAdministerAsync`), checked again at every call. The text passes `ChecklistValidator`.
- *Postconditions*: The new definition is stored, in its normal form, for that project only, with one audit entry. When
  the stored definition no longer has `expectedHash`, nothing is stored and `RecordChanged` is raised.

The service checks the right first, so a refused caller learns nothing about the validation or the current value. It
then validates the text (all errors are reported in one `ChecklistDefinitionInvalid` exception), reads the project, and
compares the hash. The normal form of the new definition (`ToJson`) is compared with the current text: equal content
returns `false` and records nothing. Different content must carry another version label than the current definition
(the default's label when the project has none); otherwise `ChecklistDefinitionInvalid` names the label. Then the
service builds the audit draft and calls `UpdateChecklistDefinitionAsync`, whose compare-and-set is atomic with the
update; when the store reports `false` the service raises `RecordChanged` with `ChangedByOthersMessage`. A caller reports
that rule as a conflict and shows the current checklist.

**ResetAsync**: Removes the definition, so the new reviews of the project use the default again.

- *Parameters*: the `Actor`, the project identity, the `expectedHash`, and a cancellation token.
- *Returns*: `Task<bool>` — `true` when the definition was removed, `false` when the project already uses the default.
- *Preconditions*: as for `SetAsync`.
- *Postconditions*: The definition is `null`, with one audit entry; a stale hash raises `RecordChanged`.

**Audit**: Every change is recorded as the single event `project.checklist-changed`, built from the actor, the project
identity (the entry carries the project and no review), and a detail that never contains the wording. A replacement says
`version V, N sections, M items, sha256 xxxxxxxx, previous version P` (the hash is the short form of
`AuditText.ShortHash`); a reset says `reset to default, version D, previous version P`. Both use the one action, because
both are a change of the checklist of the project, and the detail tells them apart. The draft is passed to the store,
which saves it in the same transaction as the change, so a change without its entry, or an entry without its change,
cannot exist, and the hash chain stays intact. A refused change records nothing. See the `AuditService` unit for the
action codes.

**Existing reviews**: A change affects only reviews created afterwards. `ReviewService` freezes a copy of the definition
into every review when it is created, and `ChecklistService` reads only that copy, so no review, approved or not, and no
record digest changes when the definition of the project changes.

#### Error Handling

Rule violations raise `DomainRuleException` before any store change: `ProjectNotFound` (the project does not exist or the
actor may not see it, with the same message for both), `ActorNotAllowed` (a plain member, an API token, or no actor),
`ChecklistDefinitionInvalid` (the text is not valid, with the paths of the first ten errors in the message, or the
version label did not change), and `RecordChanged` (the definition was changed meanwhile). Constructing the service
with a null store or null access rules, and calling `SetAsync` or `ResetAsync` with a null actor or expected hash,
raise `ArgumentNullException`. Store failures propagate unchanged.

#### Known Limitations

- The right to change the checklist is checked before the store transaction and not inside it. An administrator whose
  right is revoked within the few milliseconds between the check and the transaction of one call is not stopped for that
  one change; the next call is refused. This is the same class of limitation as for `EvidenceSettingsService`, is
  deliberate, and is not mitigated.
- The storage has no migration (see `ReviewCenterDbContext`): a development database that was created before the column
  existed does not have it and must be recreated.
- A stored definition that was valid when it was saved but can no longer be read (a changed rule, or an edit of the
  database) stops the creation of new reviews of the project with `ChecklistDefinitionInvalid`; it is never replaced by
  the default silently. A project administrator can reset or replace it.

#### Dependencies

- **IReviewStore** — reads projects (with their definition) and changes the definition with the audit entry (injected).
- **IProjectAccess** — decides who may see and who may administer the project (injected).
- **ChecklistValidator** and **DefaultChecklist** (the `ChecklistService` unit) — the validation and the default.
- **AuditDraft**, **AuditText**, and **Actor** — the audit entry and the person who made the change.

#### Callers

- **ServerHost** — registers the service as a single instance.
- The pages and endpoints that show and change the checklist of a project use it (added after this unit); `ReviewService`
  does not call it and reads the definition from the project itself when it creates a review.
