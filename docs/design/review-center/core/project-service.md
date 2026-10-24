### ProjectService

![Core Structure](CoreView.svg)

#### Purpose

`ProjectService` owns the rules for projects: it validates and normalizes project input, creates projects, and
reads them. It is also the home of the supporting types shared by the whole domain: the `Project` type, the
store interface `IReviewStore`, the rule failure type `DomainRuleException` with the `DomainRule` enumeration,
and the limits in `ReviewLimits`.

#### Data Model

**Project**: a data class (init-only properties) of `Id` (`int`, assigned by the store), `Name` (`string`), `Description`
(`string`, may be empty), and `Created` (`DateTimeOffset`, from the injected clock).

**ReviewLimits**: constants that define every limit of the domain once, so that the pages, the services, and the
storage agree:

| Constant | Value | Meaning |
| ---------- | ------- | --------- |
| `MaxFileSizeBytes` | 2 MiB | largest size of one uploaded file |
| `MaxFileCount` | 200 | most files in one review |
| `MaxTotalSizeBytes` | 20 MiB | largest total size of the files of one review |
| `MaxPathLength` | 400 | longest file path |
| `MaxProjectNameLength` | 100 | longest project name |
| `MaxProjectDescriptionLength` | 1000 | longest project description |
| `MaxReviewIdLength` | 100 | longest review id |
| `MaxReviewTitleLength` | 200 | longest review title |

**DomainRuleException**: an exception with a `Rule` (`DomainRule`) and a user-presentable message.

**IReviewStore**: the persistence interface of the domain, with the operations `AddProjectAsync`,
`GetProjectAsync`, `ListProjectsAsync`, `ReviewExistsAsync`, `AddReviewAsync`, `GetReviewAsync`,
`ListReviewsAsync`, and `GetFileContentAsync`. It is implemented by `ReviewStore` in the `Storage` subsystem.

The service holds the injected store and clock and no other state.

#### Key Methods

**CreateAsync**: Creates a project.

- *Parameters*: `string? name`, `string? description`, and a cancellation token.
- *Returns*: `Task<Project>` — the stored project with its assigned identity.
- *Preconditions*: None; invalid input is reported as a rule failure.
- *Postconditions*: The project is stored with trimmed name and description, and the creation time from the
  clock. Nothing is stored when a rule fails.

Trims both values (a null description is treated as empty), then checks that the name is not empty and not longer
than the limit (`ProjectNameInvalid`) and that the description is not longer than the limit
(`ProjectDescriptionTooLong`). Names need not be unique.

**ListAsync**: Lists all projects, ordered by name and then identity, as returned by the store.

**GetAsync**: Reads one project by identity; returns null when it does not exist.

#### Error Handling

Rule violations raise `DomainRuleException` before any store access, with `ProjectNameInvalid` or
`ProjectDescriptionTooLong`. Constructing the service with a null store or clock raises
`ArgumentNullException`. Store failures propagate unchanged.

#### Dependencies

- **IReviewStore** — persists and reads projects (injected).
- **TimeProvider** — supplies the creation time (injected).

#### Callers

- **ProjectsPage** and **ProjectPage** — create, list, and read projects.
- **ServerHost** — registers the service for the pages.
