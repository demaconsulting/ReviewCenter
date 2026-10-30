### ApiDocument

![Api Structure](ApiView.svg)

#### Purpose

`ApiDocument` describes the API in the OpenAPI format and serves the description. Its single responsibility is to make a
machine-readable contract that is generated from the real routes, is the same on every machine and run, and cannot
change
without a visible change in review.

#### Data Model

The unit is a static class with two extension methods and five constants: the address of the document
(`/api/openapi.json`), the names of the security schemes (`bearer` and `basic`), the document version (`1`), and the
operation ids of the two operations that a project can open (`GetEvidenceIndex` and `GetAttestation`). It
holds no state.

The committed snapshot `docs/api/openapi.json` is a file of the repository and not part of the program. The test that
compares the generated document with it, and that can write it when the environment variable
`REVIEWCENTER_UPDATE_OPENAPI` is set to 1, is in `ApiDocumentTests`.

**Regenerating the snapshot.** When the API changes on purpose, the snapshot is rewritten by running only the snapshot
test with the variable set (`--project` and `--filter-method` are the options of the test runner of this repository).
PowerShell:

```powershell
$env:REVIEWCENTER_UPDATE_OPENAPI = "1"
dotnet test --project test/DemaConsulting.ReviewCenter.Tests `
  --filter-method "*ApiDocument_Get_MatchesCommittedSnapshot"
Remove-Item Env:REVIEWCENTER_UPDATE_OPENAPI
```

Bash:

```bash
REVIEWCENTER_UPDATE_OPENAPI=1 dotnet test --project test/DemaConsulting.ReviewCenter.Tests \
  --filter-method "*ApiDocument_Get_MatchesCommittedSnapshot"
```

The change of `docs/api/openapi.json` is then reviewed and committed. The failure message of the test repeats the
command.

#### Key Methods

**AddApiDocument**: Registers the generator.

- *Parameters*: The service collection. Must not be null.
- *Returns*: The same collection.
- *Postconditions*: The generator describes only the routes whose path starts with `api/`, so the pages, the health
  check, and the sign-in endpoints are not part of the document. A document transformer sets the fixed parts: the title
  "ReviewCenter API", the version `1`, a description, no server list (an address would come from the request host and
  differ between machines), the HTTP bearer security scheme `bearer` with the format `rcpat`, the HTTP Basic scheme
  `basic` (the token is the
  password), and the security requirements of every operation: bearer or basic, either one. The two operations that a
  project can open (`GetEvidenceIndex` and `GetAttestation`, named by constants of the class) get a third alternative,
  an empty security requirement (`{}`, which OpenAPI defines as "no credentials"), so a client generator can
  see that a call without a token is possible and that it succeeds only when the project allows it. A schema
  transformer removes the members that the API does not require from the `required` list of the two request schemas
  (`CreateReviewRequest`: `files` and `repository`, one of which is chosen at run time; `RepositorySourceDto`:
  `baselineRevision` and `selection`, which can be left out or `null`), because the generator would list every member
  of a record without a default value as required. The response schemas are left as generated.

**MapApiDocument**: Serves the document.

- *Parameters*: The endpoint route builder. Must not be null.
- *Returns*: The same builder.
- *Postconditions*: `GET /api/openapi.json` answers the document without a token. The route is not part of the
  document itself.

#### Error Handling

None of its own. A generator that fails is the generic error of the host. The fixed version string, rather than the
version of the assembly, is deliberate: the version of a build would change the document on every build and make the
snapshot test fail without a change of the API.

#### Dependencies

- **Microsoft.AspNetCore.OpenApi** — OTS; the generator and the document model.
- **ApiEndpoints** — supplies the prefix and the metadata of the routes that are described.

#### Callers

- **ServerHost** — registers the generator and maps the document.
