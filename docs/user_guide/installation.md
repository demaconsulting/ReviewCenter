# Installation

ReviewCenter is distributed as self-contained single-file executables for win-x64 and linux-x64, so no .NET runtime
needs to be installed. Download the archive for your platform from the project's GitHub Releases page:

- `reviewcenter-<version>-win-x64.zip` (Windows)
- `reviewcenter-<version>-linux-x64.tar.gz` (Linux)

Each archive is accompanied by a `.sha256` checksum file. Verify the download before extracting it:

```bash
# Linux (run in the download folder)
sha256sum -c reviewcenter-<version>-linux-x64.tar.gz.sha256
tar -xzf reviewcenter-<version>-linux-x64.tar.gz
```

```powershell
# Windows: the printed hash must equal the value in the .sha256 file
Get-FileHash reviewcenter-<version>-win-x64.zip -Algorithm SHA256
Expand-Archive reviewcenter-<version>-win-x64.zip -DestinationPath .
```

Keep the extracted files together: the executable needs the `wwwroot` folder, the static-web-assets endpoint file
(`DemaConsulting.ReviewCenter.staticwebassets.endpoints.json`), `appsettings.json`, and the native SQLite library
(`e_sqlite3.dll` on Windows, `libe_sqlite3.so` on Linux) in the same folder. The Windows archive also holds
`aspnetcorev2_inprocess.dll`, an IIS hosting module that `reviewcenter serve` does not use. Either add the extracted
folder to your `PATH`, or run the executable by its path (`./reviewcenter-<version>-linux-x64/reviewcenter` or
`.\reviewcenter-<version>-win-x64\reviewcenter.exe`). Start the server with `reviewcenter serve`
(`reviewcenter.exe serve` on Windows) once it is on your `PATH`. No release has been published yet; until one is, build
it from source with the .NET 10 SDK:

```bash
dotnet build
dotnet run --project src/DemaConsulting.ReviewCenter -- serve
```

The examples in this guide use `reviewcenter`, the command name of the published executable; when running from
source, replace `reviewcenter` with `dotnet run --project src/DemaConsulting.ReviewCenter --`.

## Display Version

Display the tool version:

```bash
reviewcenter --version
```

## Default Behavior

When invoked with no arguments, the tool displays the banner and copyright notice:

```bash
reviewcenter
```

## Display Help

Display usage information:

```bash
reviewcenter --help
```

## Run the Web Server

The `serve` verb starts the ReviewCenter web server and keeps it running until it is stopped with Ctrl+C:

```bash
reviewcenter serve
```

By default the server listens on `http://localhost:5000`. Use `--urls <url>` to listen on another address or
port; the value must be an absolute `http` or `https` URL:

```bash
reviewcenter serve --urls http://localhost:8080
```

The server provides these routes:

- `/` - the ReviewCenter home page. It shows the title `ReviewCenter` and its tagline and is open to everyone. Once
  you are signed in it also shows a **Get started** list of links to the main pages; see
  [The Page Frame](#the-page-frame).
- `/login` - the sign-in page (open to everyone).
- `/help` and `/help/{topic}` - the built-in help (open to everyone); see [Built-in Help](#built-in-help).
- `/about` - the version and a link to the project (open to everyone).
- `/account` - your account, with the appearance (theme) setting and a form to change your password (an account at
  an identity provider has no password, so it shows a note instead).
- `/users` - the user administration page (system administrators only).
- `/users/new` - the form that creates a user (system administrators only).
- `/audit` - the audit trail (system administrators only).
- `/projects` - the list of the projects you can see.
- `/projects/new` - the form that creates a project (system administrators only).
- `/projects/{id}` - one project, with its reviews (and, for project and system administrators, its members).
- `/projects/{id}/members/new` - the form that adds an existing user to the project by the exact user name (project
  and system administrators).
- `/projects/{id}/members/account` - the form that creates a local account and adds it to the project (project and
  system administrators).
- `/projects/{id}/audit` - the audit trail of the project (project and system administrators).
- `/projects/{id}/evidence-settings` - the evidence settings of the project: anonymous access to the evidence index
  and the attestation PDFs, and what the PDFs contain (project and system administrators); see
  Evidence Settings in the Administration chapter.
- `/projects/{id}/repositories` - the git repositories of the project (project and system administrators).
- `/projects/{id}/repositories/new` - the form that adds a repository to the project (project and system
  administrators).
- `/projects/{id}/reviews/new` - the form that creates a review.
- `/projects/{id}/plan` - the page that plans reviews from the review-sets of a repository.
- `/projects/{id}/reviews/{reviewId}` - one review, with its files, their content, and comment threads.
- `/projects/{id}/reviews/{reviewId}/checklist` - the checklist of a review.
- `/projects/{id}/reviews/{reviewId}/history` - the history of a review.
- `/projects/{id}/reviews/{reviewId}/attestation.pdf` - the signed PDF attestation of an approved review; see
  Signed Attestation in the Reviewing chapter.
- `/api/...` - the JSON API; see the Automation and API Tokens chapter. Two of its routes, the
  evidence index and the attestation PDF of a project, can be opened to callers without a token by the evidence
  settings of the project; every other route needs a token.
- `/health` - a health check that answers with status 200 and the text `Healthy` while the server is running (open
  to everyone).

Every page except `/`, `/login`, `/help`, `/about`, and `/health` needs a signed-in user; see
the Users and Sign-in chapter. A project, and everything below `/projects/{id}`, is visible only to its
members and to system administrators; see Access and Visibility in the Administration chapter. The only addresses
that a project can open to visitors who are not signed in are the two API routes named above; see
Evidence Settings in the Administration chapter.

### The Page Frame

Every page has the same header and footer.

- The header shows the **ReviewCenter** brand, which is a link to the home page. When you are signed in, it is
  followed by **Projects** and, for system administrators, **Audit** and **Users**. On the right it shows
  **Help**, and then either a **Sign in** link (nobody is signed in) or your display name, an **Account** link, and a
  **Logout** button.
- The footer shows the version of ReviewCenter, the copyright, and a link to the **About** page, which has the full
  version and a link to the project.
- The home page shows the title and the tagline. When you are signed in it also shows a **Get started** card with links
  to **Projects** and, for system administrators, **Audit** and **Users**, each with a one-line
  description.

### Built-in Help

The **Help** link of the header opens `/help`, an index of short guides, open to everyone, even without signing in.
Each topic is a page of its own with links to the previous and the next topic:

- **Getting started** (`/help/getting-started`): sign in, find your project, and open a review.
- **Creating a review** (`/help/creating-a-review`): choose the files from a git repository or by uploading them.
- **Reading a review** (`/help/reading-a-review`): the Diff, Head, and Baseline views and the change kinds.
- **Comments and threads** (`/help/comments-and-threads`): comment on a line or a file, reply, resolve, and share a
  link.
- **Checklist** (`/help/checklist`): answer each check Pass, Fail, or N/A.
- **Reviewers and signing** (`/help/reviewers-and-signing`): reviewer slots, signing with your password or at your
  identity provider, and approval.
- **Signed attestation** (`/help/signed-attestation`): download a digitally signed PDF of an approved review.
- **Audit trail** (`/help/audit-trail`): every action is recorded in a tamper-evident chain.
- **Planning reviews** (`/help/planning-reviews`): create the reviews of the review-sets of a repository in one go.
- **Automation and API tokens** (`/help/api-tokens`): use a personal access token to read reviews and create drafts
  from a script, or to feed ReviewMark with the evidence index.
- **Evidence settings** (`/help/evidence-settings`): open the evidence index and the PDFs to anonymous readers and
  choose what the PDFs contain.

The topics are summaries; this guide has the details. An address below `/help` that is not a topic shows a short
message that the topic does not exist, with a link back to the index.

The `--urls` option is only valid together with `serve`; using it without `serve` is reported as an error.
The `--silent` option suppresses the server's console log output.
