# Automation and API Tokens

ReviewCenter has a JSON API under `/api` for scripts and tools, for example a build job that reads the state of
reviews or creates a draft review for a release. The API is authenticated with a **personal access token**, which
acts as the user who created it, with that user's access: the token carries the owner's project memberships (or the
system administrator flag), so the API sees only the projects the owner can see. A token is for the API only: it does
not sign in to the web
pages, and the session cookie of the web pages is not accepted by the API.

A token **never signs**. Signing a review, and adding, changing, or removing a reviewer slot, are not available in the
API: there is no route for them, and the service refuses a token even if one existed. A signature always needs a
person who enters the password again in the browser. For the same reason a token cannot **reopen** an approved review
or change the review rules of a project (there is no route, and the service refuses a token); an administrator does
both in the web pages (see Reopening an Approved Review and Review Rules in the Administration chapter).

## Create and Revoke a Token

Tokens are created on the **Account** page (see Account in the Users and Sign-in chapter): a name, an expiry of 30, 90
(the default), or
365 days, and **Create token**. The value is shown once. A token looks like `rcpat_` followed by 43 characters of a
random value (256 bits). Only the SHA-256 hash of the value is stored, so a lost token cannot be recovered; create a
new one and revoke the old one. A user may have 20 active tokens; creating a 21st is refused until one is revoked or
expires. Revoke a token on the same page. A token also stops working when its owner is disabled or locked.

Keep a token like a password: put it in the secret store of the tool that uses it, never in a repository or a command
line that is kept in a history. Revoke a token that may have been exposed.

## Call the API

Send the token as a bearer token in the `Authorization` header. This lists the projects:

```bash
curl -H "Authorization: Bearer rcpat_YOUR_TOKEN" https://reviews.example.com/api/projects
```

A client that can only send HTTP Basic credentials (ReviewMark is one) may send the token as the **password**; the user
name is ignored and may be anything, for example `reviewmark`:

```bash
curl -u reviewmark:rcpat_... https://reviews.example.com/api/projects
```

Basic credentials are accepted only under `/api`, are checked, throttled, and audited like a bearer token, and are never
logged. They do not sign in to the web pages, and the password of a person is not accepted there.

Answers are JSON (the attestation is a PDF). Errors are problem documents (`application/problem+json`) with a title and
status, and for a request that is not valid a list of the fields at fault.

| Method | Address                                         | Purpose                                          |
| ------ | ----------------------------------------------- | ------------------------------------------------ |
| GET    | `/api/projects`                                 | List your projects (all for a system admin)      |
| GET    | `/api/projects/{p}/reviews`                     | List the reviews of project `{p}`                |
| GET    | `/api/projects/{p}/reviews/{r}`                 | One review: slots, approval, progress, next step |
| POST   | `/api/projects/{p}/reviews`                     | Create a draft review (201 with the review)      |
| GET    | `/api/projects/{p}/reviews/{r}/attestation.pdf` | Signed attestation PDF (token or anonymous)      |
| GET    | `/api/projects/{p}/evidence/index.json`         | Evidence index (token or anonymous)              |
| GET    | `/api/openapi.json`                             | The OpenAPI document; open, no token needed      |

In the addresses `{p}` is the project number and `{r}` the review identifier. A project that the token's owner cannot
see is answered 404, as if it did not exist, never 403.

Two routes are an exception to the rule that every route needs a token: the evidence index and the attestation PDF.
A project administrator can open each of them to callers without a token in the evidence settings of the project (see
Evidence Settings in the Administration chapter). Both are off by default, and then a request without a token is
answered 401 as
for every other route. When one is on, a request that sends no `Authorization` header is answered for that project, with
`Cache-Control: no-store`, and at most 30 such requests a minute are accepted from one client address (more are answered
429).

The OpenAPI document describes every endpoint, the request and answer shapes, and the bearer and Basic schemes, so a
client can be generated from it. A snapshot of it is kept in the repository as `docs/api/openapi.json`.

## Use the Evidence Index with ReviewMark

ReviewMark can read the evidence of reviews from a URL instead of a file. ReviewCenter serves that evidence for a
project at `/api/projects/{p}/evidence/index.json`. A pipeline that runs `reviewmark` can then enforce that every
review-set has a current review in ReviewCenter, without keeping the evidence twice.

An example of the `evidence-source` in the `.reviewmark.yaml` file of the repository:

```yaml
evidence-source:
  type: url
  location: https://reviews.example.com/api/projects/1/evidence/index.json
  credentials:
    username-env: REVIEWCENTER_USER
    password-env: REVIEWCENTER_TOKEN
```

Put a personal access token in the secret store of the pipeline and expose it as `REVIEWCENTER_TOKEN`. ReviewMark sends
the two variables as HTTP Basic credentials, and only when both are set and not empty, so set `REVIEWCENTER_USER` to any
text (it is ignored) and keep the token in the password. Use a token of a user who can read the project; it is read-only
in this use.

**Anonymous option.** A project administrator can instead let ReviewMark read the evidence without any credentials.
The evidence settings of the project (see Evidence Settings in the Administration chapter) have two independent
switches,
**Anonymous evidence index** and **Anonymous attestation PDFs**, both off by default. ReviewMark reads the index, and
the PDFs that the index names are the evidence, so the index alone is useless to ReviewMark unless anonymous
attestation PDFs are on too, or the client has credentials. For a pipeline without credentials turn both on and leave
out the `credentials` of the `evidence-source`. Think first about who can reach the server: both switches open the
data to everyone who can, and the PDFs follow the content switches of the project.

The answer is a JSON object with a `reviews` list. Each entry has:

- `id`: the identifier of the review-set that the review was planned from;
- `fingerprint`: the content fingerprint of the review, which is the one ReviewMark computes for the files of the
  review-set (SHA-256 of the sorted SHA-256 hashes of the files);
- `date`: the UTC day (`yyyy-MM-dd`) of the last signature that completed the review;
- `result`: always `pass`;
- `file`: the absolute address of the attestation PDF of the review.

What is listed:

- Only **complete** reviews: every reviewer slot has signed the record that the review has now. A review that is not
  complete, that was changed after the signatures, or that an administrator reopened and that is not signed again, is
  not listed, so it never satisfies a review-set.
- Only reviews that were **planned from a review-set**. A review that was created by uploading files, by choosing a
  repository by hand, or through the API has no review-set identifier and is not listed.
- One entry for each identifier and fingerprint: the review that was completed last. The list is ordered by identifier
  and then by fingerprint, so the same state gives the same answer.

Limits to know about:

- There are no `fail` entries. A review with a failed answer cannot be completed, so it is simply missing.
- A review made from the **changes since a baseline** holds only the changed files, so its fingerprint is not the one
  ReviewMark computes for the whole review-set, and ReviewMark reports it as stale.
- ReviewMark only prints the `file` address. Following it needs a token (it is an API address) unless the project allows
  anonymous attestation PDFs, and records an `attestation.generated` event in the audit trail.
- The host of `file` is the `Host` of the request. Behind a reverse proxy, the proxy must keep the `Host` header,
  and `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` must be set so that the scheme is `https` (see Secure Cookie Behind a
  Reverse Proxy in the Deployment chapter).

## Create a Review with the API

`POST /api/projects/{projectId}/reviews` takes a JSON body with a review `id`, a `title`, and **exactly one** of
`files` (uploaded files, each with a `path` and its `contentBase64`) or `repository` (a `repositoryId` of an enabled
repository of that project, a `headRevision`, and optionally a `baselineRevision` and a `selection`, one pattern per
line).
The same rules as on the web pages apply: the identifier and title rules, the upload limits, and the repository rules
(see Projects and Reviews and Reviews from a Repository in the Reviewing chapter). The API refuses more than 200 `files`,
or a `contentBase64` longer than the Base64 text of 2 MiB, with a **400** that names `files` or `files[i].contentBase64`,
before it decodes anything.

```bash
curl -X POST -H "Authorization: Bearer rcpat_YOUR_TOKEN" -H "Content-Type: application/json" \
  -d '{"id":"R-1","title":"Release 1.2","files":[{"path":"src/a.txt","contentBase64":"aGVsbG8K"}]}' \
  https://reviews.example.com/api/projects/1/reviews
```

The review starts as a **Draft**, like a review created on the web pages. It is flagged
**created via API**: the review data of the API has `createdViaApi: true`, and its `review.created` audit entry names
the token (by its short identifier, never its value). A person assigns the reviewers and the reviewers sign in the
browser. The **author** of the review is the owner of the token: in a project that requires independent reviewers (the
default; see Review Rules in the Administration chapter), the owner of the token that created a review cannot be one of
its reviewers and cannot sign it, just as if the owner had created it in the browser. A review that is approved is
locked and cannot change until an administrator reopens it in the web pages (see Locked Reviews and Reopening in the
Reviewing chapter).

## Errors, Refusals, and the Throttle

- A request without a token, or with a token that is wrong, malformed, expired, or revoked, or whose owner is disabled or
  locked, always gets the same **401 Unauthorized** problem document with `WWW-Authenticate` headers for `Bearer` and
  `Basic`. The answer never says which of these it was, so it does not reveal which tokens exist. The reason is
  recorded in the audit trail.
- Refused attempts are counted per client address with the same throttle as the sign-in (see Sign In and Out in the
  Users and Sign-in chapter and its
  `Identity:Throttle:*` settings), separately from the web sign-in. An address that has used up its attempts is
  answered **429 Too many requests** for the rest of the window, even with a good token. A good token does not use up
  attempts.
- A request that is not valid is answered **400** with the fields at fault; a project, review, or repository that does
  not exist (or that the token's owner cannot see) **404**; a review identifier that is already used, a disabled
  repository, an attestation of a review that is not complete, or a change that is refused because the review is
  approved and locked (or a reopen of a review that is not locked) **409** (a request without a token, possible on the
  anonymous attestation route, gets **404** instead); and a git repository that is not reachable
  **502**, **503**, or **504**.
- A **locked review** stays readable. `GET` of the review shows `approved: true`, and its attestation PDF and the
  evidence index keep working; the wire format has not changed. The API has no route that changes a thread, a checklist
  answer, or a reviewer slot, so a token does not meet the lock today. If a refusal because of the lock is reported, it
  is a **409** problem document whose detail is "This review is approved and locked. A project administrator must reopen
  it before it can change.". There is no reopen route in the API.
- Any other address under `/api` is answered with a JSON **404**, with or without a token, for every standard HTTP
  method (`GET`, `HEAD`, `POST`, `PUT`, `PATCH`, `DELETE`, `OPTIONS`).
- The server behind a reverse proxy needs the setting described in Secure Cookie Behind a Reverse Proxy (in the
  Deployment chapter), or all clients
  share one address and one throttle.

The audit trail records `token.created` (with the name, the short identifier, and the expiry of the token) and
`token.revoked` (with the name and the short identifier), never the value, and `token.use-failed` and
`token.use-throttled` for refused uses (see Recorded Actions in the Administration chapter). A token cannot create
or revoke tokens; that is done on
the Account page only. Refusals are
recorded at most once a minute for an address, so a flood cannot fill the trail. The last-used time of a token is
updated at most once every 5 minutes.
