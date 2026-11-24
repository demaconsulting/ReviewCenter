# build.ps1
#
# PURPOSE:
#   Builds the solution in Release configuration and runs the unit tests, either all of them
#   (-All, the full local build and test run) or only the test projects a change affects
#   (-Changed / -Project).
#
# USAGE:
#   pwsh ./build.ps1 -Help                     Print the usage text and exit 0
#   pwsh ./build.ps1 -All                      Full local build and all tests
#   pwsh ./build.ps1 -Changed [-Base <ref>]    Only the test projects affected by the changes since <ref>
#                                              (default origin/main, else main) plus uncommitted changes
#   pwsh ./build.ps1 -Project Core             Only the test projects affected by the named projects (full or short
#                                              names: App is the web application, Core the core library, other short
#                                              names are relative to the application, so Tests is
#                                              DemaConsulting.ReviewCenter.Tests; -Help lists them)
#   Add -DryRun to any scope to print the selection without building anything.
#   With no arguments the usage text is printed and the script exits 1 (a scope must be chosen).
#
#   A changed project selects every project that references it, directly or transitively, from the
#   ProjectReference graph, and files linked into another project (Compile/None/EmbeddedResource items
#   that point outside the project folder) select that project too. The benchmarks/ projects are part of the
#   graph (built only, like the tool projects); the Puppet tool tests under tools/ are tested. Changes that affect the whole
#   build (solution, project, props/targets, global.json, .editorconfig, nuget.config, tool manifest,
#   this script, scripts/ProjectScripts.psm1) fall back to the full build; repository files tests read
#   at run time select their test projects through $RuntimeReads; other changes the build does not
#   read select nothing. This works unchanged for a solution with one project pair or many.
#
# EXTENSION POINTS:
#   Search for "[PROJECT-SPECIFIC]" comments to find the designated locations
#   for adding project-specific build or test operations.
#
# CUSTOMIZATION:
#   This script belongs to the project: extend it as the project grows. Add steps at the
#   [PROJECT-SPECIFIC] points and add optional parameters that focus a run. The reusable scoping
#   logic lives in scripts/ProjectScripts.psm1, which is delivered by the Agents release zip, is
#   shared across repositories, and must never be edited here. Slow or environment-dependent test
#   tiers (GUI automation, integration) may be opt-in switches.
#   With no arguments the script prints the usage text and exits 1; -All must still build and run
#   the standard test suite (agents rely on that), and never remove or weaken a test step to make
#   it pass.

param(
    # Full local build and all tests.
    [switch]$All,

    # Build and test only the projects affected by the changed files.
    [switch]$Changed,

    # Git reference to compare against for -Changed (default: origin/main, else main).
    [string]$Base,

    # Build and test only the projects affected by these projects (full or short names, e.g. Core, Tests).
    [string[]]$Project,

    # Print the selection and exit without building.
    [switch]$DryRun,

    # Print the usage text and exit 0.
    [switch]$Help
)

Import-Module (Join-Path $PSScriptRoot 'scripts/ProjectScripts.psm1') -Force

# [PROJECT-SPECIFIC] Repository files that tests read at run time (through the repository root, not through a
# project item), mapped to the test project that reads them. Keep in step with the tests.
$RuntimeReads = [ordered]@{
    # ApiDocumentTests compares the served OpenAPI document with the checked-in copy
    '^docs/api/openapi\.json$' = 'DemaConsulting.ReviewCenter.Tests'
}

# [PROJECT-SPECIFIC] Project folders outside src/, test/, and tools/ that belong to the build graph, so -Changed sees them.
$GraphRoots = @('src', 'test', 'tools', 'benchmarks')

# [PROJECT-SPECIFIC] Short -Project names that Resolve-ProjectName cannot derive: it treats the shortest src/ project (the
# web application) as 'Core' and as the prefix of the other short names, but the core library is DemaConsulting.ReviewCenter.Core.
$ProjectAliases = @{
    'Core' = 'DemaConsulting.ReviewCenter.Core'
    'App'  = 'DemaConsulting.ReviewCenter'
}

# [PROJECT-SPECIFIC] Test projects: the projects that are run with dotnet test (the Puppet tool tests live under tools/).
# Every other selected project under tools/ or benchmarks/ is built only; libraries under src/ and test/ are built by
# the projects that reference them.
function Test-TestProject {
    param($Entry)
    return $Entry.Name -match '\.(Tests|SystemTests)$'
}

# Resolves a -Project name through the aliases first, then the shared resolver
function Resolve-ReviewCenterProject {
    param($Graph, [string]$Name)
    $alias = $ProjectAliases.Keys | Where-Object { $_ -ieq $Name } | Select-Object -First 1
    if ($alias) { return $ProjectAliases[$alias] }
    return Resolve-ProjectName $Graph $Name
}

# ==============================================================================
# USAGE AND SCOPE VALIDATION
# ==============================================================================

function Show-Usage {
    Write-ScriptUsage -Script 'build.ps1' -IterateWith '-Changed (or -Project)' `
        -Purpose 'build the solution (Release) and run the unit tests.' `
        -Options ([ordered]@{
            '-All'             = 'Full local build and all tests; cannot be combined with another scope.'
            '-Changed'         = 'Only the test projects affected by files changed since the base, plus uncommitted files.'
            '-Base <ref>'      = 'Base reference for -Changed (default origin/main, else main).'
            '-Project <names>' = 'Only the test projects affected by these projects (full or short names, e.g. Tests).'
            '-DryRun'          = 'Print what would be built, tested, and skipped, then exit 0 without running anything.'
            '-Help'            = 'Print this text and exit 0.'
        }) `
        -ChecksLabel 'Projects (-Project names; Core is the core library, App the web application)' `
        -Checks @((@(try { (Get-ProjectGraph -Root $GraphRoots).Keys } catch { }) + $ProjectAliases.Keys) | Sort-Object -Unique) `
        -Examples @('-Changed', '-Project Core', '-Changed -DryRun', '-All')
}

if ($Help) { Show-Usage; exit 0 }

$scopeProblems = Test-ScopeArguments -Base $Base -Scopes ([ordered]@{
        All     = $All.IsPresent
        Changed = $Changed.IsPresent
        Project = [bool]$Project
    })
if ($scopeProblems.Count -gt 0) {
    $scopeProblems | ForEach-Object { Write-Host "Error: $_" }
    Write-Host ""
    Show-Usage
    exit 1
}

$buildError = $false

# ==============================================================================
# SCOPED BUILD (-Changed / -Project)
# Selects the affected test projects; falls back to the full build when needed.
# ==============================================================================

$scoped = $false
if (-not $All) {
    $graph = Get-ProjectGraph -Root $GraphRoots
    $selected = [System.Collections.Generic.HashSet[string]]::new()
    $fullReason = $null

    $names = @($Project | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($Project -and $names.Count -eq 0) {
        Write-Host "Error: no project named in '$($Project -join ' ')'. Known projects: $($graph.Keys -join ', ')"
        exit 1
    }
    foreach ($single in $names) {
        $resolved = Resolve-ReviewCenterProject $graph $single
        if (-not $resolved) {
            Write-Host "Unknown project '$single'. Known projects: $($graph.Keys -join ', ')"
            exit 1
        }
        [void]$selected.Add($resolved)
    }

    if ($Changed) {
        $changedFiles = Get-ChangedFiles -Base $Base
        if ($null -eq $changedFiles) {
            $fullReason = 'the changed files could not be determined'
        } else {
            Write-Host "Changed files: $($changedFiles.Count)"
            $fullReason = Add-ChangedProjects -Graph $graph -Files $changedFiles -Selected $selected -RuntimeReads $RuntimeReads
        }
    }

    if ($fullReason) {
        Write-Host "Full build required: $fullReason."
    } else {
        Expand-ReferencingProjects -Graph $graph -Selected $selected

        # Test projects are built and tested; the other selected projects under tools/ and benchmarks/ are built only
        $tests = @($graph.Values | Where-Object { $selected.Contains($_.Name) -and (Test-TestProject $_) })
        $tools = @($graph.Values | Where-Object { $selected.Contains($_.Name) -and -not (Test-TestProject $_) -and $_.Directory -match '^(tools|benchmarks)/' })
        $skipped = @($graph.Values | Where-Object { (Test-TestProject $_) -and -not $selected.Contains($_.Name) })
        if ($tests.Count -eq 0 -and $tools.Count -eq 0) {
            Write-Host "Nothing to build: no changed file is read by the build."
            exit 0
        }

        Write-Host "Affected test projects (built and tested):"
        $tests | ForEach-Object { Write-Host "  $($_.Name)" }
        if ($tools.Count -gt 0) {
            Write-Host "Affected tool projects (build only):"
            $tools | ForEach-Object { Write-Host "  $($_.Name)" }
        }
        if ($DryRun) {
            Write-Host "Skipped test projects ($($skipped.Count)): $(($skipped | ForEach-Object { $_.Name }) -join ', ')"
            Write-Host "Dry run: nothing was built or tested."
            exit 0
        }

        $scoped = $true
        foreach ($entry in $tests + $tools) {
            Write-Host "Building $($entry.Name)..."
            dotnet build $entry.Path --configuration Release
            if ($LASTEXITCODE -ne 0) { $buildError = $true }
        }

        if (-not $buildError) {
            foreach ($entry in $tests) {
                Write-Host "Running tests: $($entry.Name)..."
                dotnet test --project $entry.Path --no-build --configuration Release
                if ($LASTEXITCODE -ne 0) { $buildError = $true }
            }
        } else {
            Write-Host "Skipping tests: the build failed."
        }
    }
}

if ($DryRun -and -not $scoped) { Write-Host "Dry run: would run the full build and all tests."; exit 0 }

# ==============================================================================
# FULL BUILD (-All, or a scoped run that needs everything)
# ==============================================================================

if (-not $scoped) {
    # Each step runs only if the steps before it succeeded, so tests never run against stale binaries
    Write-Host "Restoring dependencies..."
    dotnet restore
    if ($LASTEXITCODE -ne 0) { $buildError = $true }

    if (-not $buildError) {
        Write-Host "Building..."
        dotnet build --no-restore --configuration Release
        if ($LASTEXITCODE -ne 0) { $buildError = $true }
    }

    if (-not $buildError) {
        Write-Host "Running tests..."
        # Microsoft.Testing.Platform runner (global.json): no --logger trx; the browser system tests skip locally when
        # Chromium is missing (CI installs it and sets REVIEWCENTER_REQUIRE_CHROMIUM=true)
        dotnet test --no-build --configuration Release
        if ($LASTEXITCODE -ne 0) { $buildError = $true }
    } else {
        Write-Host "Skipping tests: the build failed."
    }

    # [PROJECT-SPECIFIC] Add additional build steps here (e.g., packaging, publishing).
    # Guard steps that depend on the build with: if (-not $buildError) { ... }
}

if (-not $buildError) {
    Write-Host "Build and tests completed successfully!"
}

exit ($buildError ? 1 : 0)
