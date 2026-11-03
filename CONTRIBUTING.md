# Contributing to ReviewCenter

Thank you for your interest in contributing to ReviewCenter! We welcome contributions from the community and
appreciate your help in making this project better.

## Code of Conduct

This project adheres to a [Code of Conduct][code-of-conduct]. By participating, you are expected to uphold this code.
Please report unacceptable behavior through GitHub.

## How to Contribute

### Reporting Bugs

If you find a bug, please create an issue on GitHub with the following information:

- **Description**: Clear description of the bug
- **Steps to Reproduce**: Detailed steps to reproduce the issue
- **Expected Behavior**: What you expected to happen
- **Actual Behavior**: What actually happened
- **Environment**: Operating system, .NET version, ReviewCenter version
- **Logs**: Any relevant error messages or logs

### Suggesting Features

We welcome feature suggestions! Please create an issue on GitHub with:

- **Feature Description**: Clear description of the proposed feature
- **Use Case**: Why this feature would be useful
- **Proposed Solution**: Your ideas on how to implement it (optional)
- **Alternatives**: Any alternative solutions you've considered (optional)

### Submitting Pull Requests

We follow a standard GitHub workflow for contributions:

1. **Fork** the repository
2. **Clone** your fork locally
3. **Create a branch** for your changes (`git checkout -b feature/my-feature`)
4. **Make your changes** following our coding standards
5. **Test your changes** thoroughly
6. **Commit your changes** with clear commit messages
7. **Push** to your fork
8. **Create a Pull Request** to the main repository

## Development Setup

### Prerequisites

- [.NET SDK][dotnet-download] 8.0, 9.0, or 10.0
- Git
- A code editor (Visual Studio, VS Code, or Rider recommended)

### Getting Started

1. Clone the repository:

   ```bash
   git clone https://github.com/demaconsulting/ReviewCenter.git
   cd ReviewCenter
   ```

2. Restore dependencies:

   ```bash
   dotnet tool restore
   dotnet restore
   ```

3. Build the project:

   ```bash
   dotnet build --configuration Release
   ```

4. Run unit tests:

   ```bash
   dotnet test --configuration Release
   ```

## Coding Standards

### General Guidelines

- Follow the [C# Coding Conventions][csharp-conventions]
- Use clear, descriptive names for variables, methods, and classes
- Write XML documentation comments for all public, internal, and private members
- Keep methods focused and single-purpose
- Write tests for new functionality

### Code Style

This project enforces code style through `.editorconfig`. Key requirements:

- **Indentation**: 4 spaces for C#, 2 spaces for YAML/JSON/XML
- **Line Endings**: LF (Unix-style)
- **Encoding**: UTF-8
- **Namespaces**: Use file-scoped namespace declarations
- **Braces**: Required for all control statements
- **Naming Conventions**:
  - Interfaces: `IInterfaceName`
  - Classes/Structs/Enums: `PascalCase`
  - Methods/Properties: `PascalCase`
  - Parameters/Local Variables: `camelCase`

### XML Documentation

All members require XML documentation with proper indentation:

```csharp
/// <summary>
///     Brief description of what this does.
/// </summary>
/// <param name="parameter">Description of the parameter.</param>
/// <returns>Description of the return value.</returns>
public int ExampleMethod(string parameter)
{
    // Implementation
}
```

Note the spaces after `///` for proper indentation in summary blocks.

## Testing Guidelines

### Test Framework

We use xUnit v3 for unit and integration tests.

### Test Naming Convention

Use the pattern: `ClassName_MethodUnderTest_Scenario_ExpectedBehavior`

Examples:

- `Program_Main_NoArguments_ReturnsSuccess`
- `Context_Create_WithInvalidFlag_ThrowsArgumentException`
- `Validation_Run_WithSilentContext_ExitCodeIsZero`

### Writing Tests

- Write tests that are clear and focused
- Use xUnit v3 assertions:
  - `Assert.Single(collection)` and `Assert.Equal(expectedCount, collection.Count)`
  - `Assert.Empty(collection)`
  - `Assert.DoesNotContain(item, collection)`
- Always clean up resources (use `try/finally` for console redirection)
- Link tests to requirements in `requirements.yaml` when applicable

### Running Tests

#### Unit Tests

```bash
# Run all unit tests
dotnet test --configuration Release

# Run specific unit test
dotnet test --filter "FullyQualifiedName~YourTestName"

# Run with coverage
dotnet test --collect "XPlat Code Coverage"
```

#### Self-Validation Tests

```bash
# Run self-validation tests
dotnet run --project src/DemaConsulting.ReviewCenter \
  --configuration Release --framework net10.0 --no-build -- --validate
```

### Regenerating the API snapshot

The OpenAPI description of the API is kept in `docs/api/openapi.json`, and a test fails when the generated description
differs from it. When you change the API on purpose, rewrite the snapshot by running only that test with the environment
variable `REVIEWCENTER_UPDATE_OPENAPI` set to `1`.

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

Clear the variable afterwards (the PowerShell example does), because the test only writes while it is set. Then review
`git diff docs/api/openapi.json` and commit the file together with the API change; CI fails on a stale snapshot.

## Documentation

### Markdown Guidelines

All markdown files must follow these rules (enforced by markdownlint):

- Maximum line length: 120 characters
- Use ATX-style headers (`# Header`)
- Lists must be surrounded by blank lines
- Use reference-style links: `[text][ref]` with `[ref]: url` at document end
- **Exceptions**:
  - `README.md` uses absolute URLs (it's the project landing page and is read outside the repository)
  - AI agent markdown files (`.github/agents/*.agent.md`) use inline links
    `[text](url)` so URLs are visible in agent context

### Spell Checking

All files are spell-checked using cspell. **Never** add a word to the `.cspell.yaml` word list in order to silence a
spell-checking failure. Doing so defeats the purpose of spell-checking and reduces the quality of the repository.

- If cspell flags a word that is **misspelled**, fix the spelling in the source file.
- If cspell flags a word that is a **genuine technical term** (tool name, project identifier, etc.) and is spelled
  correctly, raise a **proposal** (e.g. comment in a pull request) explaining why the word should be added. The
  proposal must be reviewed and approved before the word is added to the list.

## Quality Checks

Before submitting a pull request, ensure all quality checks pass:

### 1. Build and Test

```bash
# Build the project
dotnet build --configuration Release

# Run unit tests
dotnet test --configuration Release
```

All tests must pass with zero warnings.

### 2. Linting

```pwsh
# After making changes: applies dotnet format, markdown, and YAML fixes silently
pwsh ./fix.ps1

# Before submitting a pull request: all linters must pass
pwsh ./lint.ps1
```

### 3. Code Coverage

Maintain or improve code coverage. Use the `--collect "XPlat Code Coverage"` option when running tests.

## Commit Messages

Write clear, concise commit messages:

- Use present tense ("Add feature" not "Added feature")
- Use imperative mood ("Move cursor to..." not "Moves cursor to...")
- Limit first line to 72 characters
- Reference issues and pull requests when applicable

Examples:

- `Add support for custom report headers`
- `Fix crash when results file path is invalid`
- `Update documentation for --report-depth option`
- `Refactor argument parsing for better testability`

## Pull Request Process

1. **Update Documentation**: Update relevant documentation for your changes
2. **Add Tests**: Include tests that cover your changes
3. **Run Quality Checks**: Ensure all linters, tests, and builds pass
4. **Submit PR**: Create a pull request with a clear description
5. **Code Review**: Address feedback from maintainers
6. **Merge**: Once approved, a maintainer will merge your PR

### Pull Request Template

When creating a pull request, include:

- **Description**: What changes does this PR introduce?
- **Motivation**: Why are these changes needed?
- **Related Issues**: Link to any related issues
- **Testing**: How have you tested these changes?

- #### Checklist

  - [ ] Tests added/updated
  - [ ] Documentation updated
  - [ ] All tests pass
  - [ ] Code follows style guidelines
  - [ ] No new warnings introduced

## Requirements Management

ReviewCenter uses [DemaConsulting.ReqStream][reqstream] for requirements traceability:

- All requirements are defined in `requirements.yaml`
- Each requirement should be linked to test cases
- Run `dotnet reqstream` to generate requirements documentation
- Use the `--enforce` flag to ensure all requirements have test coverage

### Atomic Requirements

Every requirement states one testable criterion (formal-review check REQ-07). A requirement that bundles several
behaviors is split, and nothing is lost: every old id stays, every behavior stays covered by a test.

#### Rules

- One `shall` is one criterion: one observable outcome, one reason for a test to fail.
- Do not join separate obligations with "and" or "or". A condition that defines the single behavior ("when the
  project requires independent reviewers") stays in the sentence.
- Separately testable alternatives, exceptions, error cases, and side effects (an audit entry, nothing stored)
  are their own requirements.
- An enumeration of items that share one condition and one outcome, and are checked by one parameterized test
  (for example "add, change, or remove a slot"), is one criterion. So are the fields of one stored or shown record
  (for example the user, time, and reason in one audit entry) that one test checks together.
- Split on what the old text states. Do not invent obligations that the code and the tests do not support, and do
  not split what the unit cannot tell apart. Report real gaps instead of hiding them.
- Never delete a requirement or drop a criterion.

#### Naming

- The first or main criterion keeps the old id, so parent `children` lists, prose, and review reports stay valid.
- Every other criterion gets `<OldId>-<Aspect>`: a short PascalCase noun phrase, usually one to three words, unique
  within the file (for example `ReviewCenter-SigningService-Sign-Alternate`).
- Title: one sentence, "The <Class> class shall ...". Each requirement has its own short `justification`.

#### Test linking

- Link each requirement only to existing own-level tests that really verify that criterion. One test may be linked
  to several requirements, and every old test link must stay linked by at least one new requirement.
- When a criterion has no dedicated test, add a small focused test named `{Class}_{Method}_{Scenario}_{Expected}`
  and a scenario entry for it in the verification document. Do not change source code.
- Do not turn tests that merely exist into requirements. Link them to the nearest stated criterion.

#### Parents and references

- In every parent requirement (in `docs/reqstream/**`, for example `core.yaml`), list the new ids in `children`
  next to the old id. Links flow downward only. `requirements.yaml` only includes files and does not change.
- A prose reference such as "see the Concluded requirement" names the exact new id.
- Parent and system-level requirements bundle behaviors too. Split them last, so that every new child keeps a
  parent, and keep each child under at least one parent.

#### Before and after

```yaml
# Before: one requirement, two criteria
- id: ReviewCenter-Example-Slots
  title: >-
    The Example class shall number slots in the order added and reject a slot for a missing review.
  tests: [Example_AddSlot_Valid_Numbered, Example_AddSlot_MissingReview_Throws]

# After: the old id keeps the main criterion
- id: ReviewCenter-Example-Slots
  title: The Example class shall number slots in the order in which they were added.
  justification: The reviewers refer to a slot by its number.
  tests: [Example_AddSlot_Valid_Numbered]
- id: ReviewCenter-Example-Slots-ReviewExists
  title: The Example class shall reject a slot for a review that does not exist.
  justification: A slot without a review would be lost.
  tests: [Example_AddSlot_MissingReview_Throws]
```

#### Agent checklist

1. Read the old requirement and list its criteria. Look for "and", "or", commas, "even when", "except",
   "otherwise", "also", and "at the same time".
2. Read the linked tests and decide which criterion each one verifies.
3. Write the atomic requirements, then add the new ids to every parent `children` list.
4. Add focused tests only for real gaps, plus their verification scenarios.
5. Run `dotnet reqstream --requirements requirements.yaml --lint`, the targeted tests, and
   `--tests "artifacts/*.trx" --enforce`. No id of the unit may be unsatisfied.
6. Run `pwsh ./fix.ps1` then `pwsh ./lint.ps1`.

#### Common pitfalls

- Test names in the TRX file of parameterized tests carry their arguments, so compare by method name.
- A unit's tests can live in several test classes. Include each class when running the targeted tests.
- Check the indentation of `children` lists after each edit. A wrong indent shows as many "unknown child" errors.
- Files made with the `create` tool may use CRLF, but the repository uses LF. Edit existing files where possible.
- Linking one broad test to many criteria hides gaps. Prefer a small focused test.
- A unit requirement states observable behavior of that unit. Do not claim a lower-level property (such as a
  storage transaction) that only a lower-level test can show, and do not name a helper (HOW). Put helper reuse in
  the design document and link the storage property at the storage unit.

## Release Process

Releases are managed by project maintainers. The process includes:

1. Version bump in project files
2. Tag the release in Git
3. Build and test across all supported platforms
4. Publish the self-contained win-x64 and linux-x64 executables as archives with SHA-256 checksums
5. Create GitHub release with the archives, checksums, documents, and release notes

## Getting Help

- **Questions**: Use [GitHub Discussions][discussions]
- **Bugs**: Report via [GitHub Issues][issues]
- **Security**: See [SECURITY.md][security] for vulnerability reporting

## License

By contributing to ReviewCenter, you agree that your contributions will be licensed under the MIT License.

Thank you for contributing to ReviewCenter!

[code-of-conduct]: https://github.com/demaconsulting/ReviewCenter/blob/main/CODE_OF_CONDUCT.md
[dotnet-download]: https://dotnet.microsoft.com/download
[csharp-conventions]: https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions
[reqstream]: https://github.com/demaconsulting/ReqStream
[discussions]: https://github.com/demaconsulting/ReviewCenter/discussions
[issues]: https://github.com/demaconsulting/ReviewCenter/issues
[security]: https://github.com/demaconsulting/ReviewCenter/blob/main/SECURITY.md
