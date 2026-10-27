### ApiDocument

![Api Structure](ApiView.svg)

#### Purpose

`ApiDocument` describes the API in the OpenAPI format and serves the description. Its single responsibility is to make a
machine-readable contract that is generated from the real routes, is the same on every machine and run, and cannot change
without a visible change in review.

#### Data Model

The unit is a static class with two extension methods and three constants: the address of the document
(`/api/openapi.json`), the name of the security scheme (`bearer`), and the document version (`1`). It holds no state.

The committed snapshot `docs/api/openapi.json` is a file of the repository and not part of the program. The test that
compares the generated document with it, and that can write it when the environment variable
`REVIEWCENTER_UPDATE_OPENAPI` is set to 1, is in `ApiDocumentTests`.

#### Key Methods

**AddApiDocument**: Registers the generator.

- *Parameters*: The service collection. Must not be null.
- *Returns*: The same collection.
- *Postconditions*: The generator describes only the routes whose path starts with `api/`, so the pages, the health
  check, and the sign-in endpoints are not part of the document. A document transformer sets the fixed parts: the title
  "ReviewCenter API", the version `1`, a description, no server list (an address would come from the request host and
  differ between machines), the HTTP bearer security scheme `bearer` with the format `rcpat`, and the security
  requirement of every operation.

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
