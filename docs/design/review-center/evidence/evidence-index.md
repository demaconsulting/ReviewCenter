### EvidenceIndexService

![Evidence Structure](EvidenceView.svg)

#### Purpose

`EvidenceIndexService` builds the evidence index of a project: the list of its completed reviews in the form that
ReviewMark reads. Its single responsibility is the choice of which reviews are listed and how they are named, dated, and
ordered. It makes no decision about what a complete review is (that is `SigningService`), does not know the format of
the JSON document, and does not know the address of a file (that is `ApiEndpoints`).

#### Data Model

The service is stateless and thread-safe. It is created with the review service, the signing service, the project
access rules, and the evidence settings service. Its result is a
list of `EvidenceIndexEntry` records: the `Id` (the review-set id of the review, which is the ReviewMark review id), the
`Fingerprint` (the content fingerprint of the review), the `Date` (the UTC day of the latest signature as `yyyy-MM-dd`),
and the `ReviewId` of the review that the entry stands for, which the endpoint uses to build the address of the
attestation. Nothing is stored: the index is built for each request.

#### Key Methods

**BuildAsync**: Builds the index of a project.

- *Parameters*: The `Actor?` who asks (`null` for a request without a signed-in user), an `int projectId`, and a
  cancellation token.
- *Returns*: the entries, one for each review-set id and fingerprint, ordered by id and then by fingerprint (ordinal);
  an empty list when no review qualifies.
- *Postconditions*: First the asker is checked. With an actor, `IProjectAccess.RequireViewAsync` is called, so an actor
  who may not view the project gets the rule of a missing project and nothing is read. Without an actor, the effective
  evidence settings of the project are read (`EvidenceSettingsService.GetEffectiveAsync`), and unless the project
  exists and has `AnonymousIndex` on the rule of a missing project is thrown, the same as for a project that does not
  exist, and nothing else is read. A review is considered only when it has a review-set id. Its approval
  is read from `SigningService` (all in one batch, see Cost);
  a review that has no approval, or whose approval is not complete (no slot, an unsigned slot, or a signature of an
  older record), is not listed. The completion time of a review is the latest `SignedAt` of its slots. Of the reviews
  with the same id and fingerprint the one with the latest completion time is kept and, for the same time, the one with
  the lowest review id (ordinal). The result is always a pass, so the entry has no result field: the endpoint writes
  `pass`.

#### Error Handling

A review that cannot be read as complete is not listed, and no review causes an error. A null argument to the
constructor raises `ArgumentNullException`. A null actor is not an error: it is an anonymous request, which is checked
against the project switch (see `BuildAsync`). Exceptions of the stores reach the caller.

**Why only passes**: A review with a failed checklist answer cannot be signed, so no completed review has failed. The
index therefore never holds a `fail` entry, and ReviewMark shows a review without a current entry as missing or stale.

**Why reviews without a review-set id are skipped**: ReviewMark matches an entry by the review-set id of its
configuration. Only a review that was planned from a review-set (see `ReviewPlanner`) has one. A review that was made by
upload, by choosing a repository by hand, or through the API has none, and a made-up id could wrongly satisfy a
review-set that nobody reviewed.

**Cost**: The approvals of all reviews that have a review-set id are read with one call of
`SigningService.GetApprovalsAsync`, which loads the user list once for the whole index and not once for each review, and
reads the snapshots of all the reviews with one set-based store read (`IReviewStore.ListSnapshotsAsync`), so the cost
does not grow with a read per review.

#### Dependencies

- **ReviewService**, **SigningService**, **ReviewApproval**, **IProjectAccess**, **EvidenceSettingsService** — Core. The
  completion rule is that of
  the attestation: every slot signed with the current record digest.

#### Callers

- **ApiEndpoints** — writes the index of a project as the JSON of the evidence source of ReviewMark.
