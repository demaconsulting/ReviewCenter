# lint.ps1
#
# PURPOSE:
#   Runs all lint checks and reports failures. Exits 1 on error.
#   Used by CI/CD as the merge gate and by the lint-fix agent
#   during pre-PR cleanup.
#
#   To auto-fix formatting issues, run fix.ps1 instead.
#
# EXTENSION POINTS:
#   Search for "[PROJECT-SPECIFIC]" comments to add project-specific checks.
#
# MODIFICATION POLICY:
#   Only modify this file to add project-specific operations at the designated
#   [PROJECT-SPECIFIC] extension points, or to update tool versions as needed.

function Get-VenvActivateScript {
    if (Test-Path ".venv/Scripts/Activate.ps1") { return ".venv/Scripts/Activate.ps1" }
    if (Test-Path ".venv/bin/Activate.ps1") { return ".venv/bin/Activate.ps1" }
    return $null
}

function Initialize-PythonVenv {
    if (-not (Test-Path ".venv")) {
        python -m venv .venv
        if ($LASTEXITCODE -ne 0) { return $false }
    }

    $activateScript = Get-VenvActivateScript
    if (-not $activateScript) { return $false }
    & $activateScript
    if (-not (Get-Command deactivate -ErrorAction SilentlyContinue)) { return $false }

    $installSucceeded = $false
    try {
        pip install -r pip-requirements.txt --quiet --disable-pip-version-check
        $installSucceeded = $LASTEXITCODE -eq 0
        return $installSucceeded
    }
    finally {
        if (-not $installSucceeded -and (Get-Command deactivate -ErrorAction SilentlyContinue)) {
            deactivate 2>$null
        }
    }
}

$lintError = $false

# --- YAML ---
Write-Host "Linting: YAML..."
$skipPython = -not (Initialize-PythonVenv)
if ($skipPython) { $lintError = $true }

if (-not $skipPython) {
    yamllint .
    if ($LASTEXITCODE -ne 0) { $lintError = $true }
    deactivate
}

# --- dotnet Tools ---
$skipDotnetTools = $false
dotnet tool restore > $null
if ($LASTEXITCODE -ne 0) { $lintError = $true; $skipDotnetTools = $true }

# --- Spelling ---
Write-Host "Linting: spelling..."
if (-not $skipDotnetTools) {
    dotnet textlint
    if ($LASTEXITCODE -ne 0) { $lintError = $true }
}

# --- Markdown ---
Write-Host "Linting: markdown..."
if (-not $skipDotnetTools) {
    dotnet lintmark
    if ($LASTEXITCODE -ne 0) { $lintError = $true }
}

# [PROJECT-SPECIFIC] Add additional lint checks here.

# --- Source Rules ---
# Rules about the text of the product sources. They are properties of the source, not behavior, so they are lint
# checks and not tests: the audit action vocabulary (AuditActions.cs) lists exactly the actions the code records, and
# the API reads that decide what a viewer may see always name the actor.
Write-Host "Linting: source rules..."
$productSources = @('src/DemaConsulting.ReviewCenter.Core', 'src/DemaConsulting.ReviewCenter') |
    ForEach-Object { Get-ChildItem $_ -Recurse -Filter *.cs } |
    Where-Object { $_.FullName -notmatch '[/\\](bin|obj|generated)[/\\]' }
$vocabularyFile = $productSources | Where-Object Name -eq 'AuditActions.cs' | Select-Object -First 1
$vocabularyList = [regex]::Match((Get-Content $vocabularyFile.FullName -Raw), 'All \{ get; \} =\s*\[(?<list>[^\]]*)\]').Groups['list'].Value
$vocabulary = @([regex]::Matches($vocabularyList, '"([a-z]+\.[a-z]+(?:-[a-z]+)*)"') | ForEach-Object { $_.Groups[1].Value })
$prefixes = @($vocabulary | ForEach-Object { $_.Substring(0, $_.IndexOf('.') + 1) } | Sort-Object -Unique)
$actionPatterns = @(
    '(?:AuditDraft\.By\(|new AuditDraft\()(?:[^"]|\n)*?"([a-z]+\.[a-z]+(?:-[a-z]+)*)"',
    'Action\w*\s*=\s*"([a-z]+\.[a-z]+(?:-[a-z]+)*)"',
    ('"((?:' + (($prefixes | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')[a-z-]+)"'))
$recorded = @($productSources | Where-Object Name -ne 'AuditActions.cs' | ForEach-Object {
        $text = Get-Content $_.FullName -Raw
        foreach ($pattern in $actionPatterns) { [regex]::Matches($text, $pattern) | ForEach-Object { $_.Groups[1].Value } }
    } | Where-Object { -not $_.StartsWith('selftest.') } | Sort-Object -Unique)
$missing = @($recorded | Where-Object { $vocabulary -cnotcontains $_ })
$stale = @($vocabulary | Where-Object { $recorded -cnotcontains $_ })
if ($vocabulary.Count -lt 40 -or $recorded.Count -lt 40) {
    Write-Host "Audit actions: the scan found too few actions to be trusted; the patterns are out of date."
    $lintError = $true
}
foreach ($action in $missing) { Write-Host "Audit actions: '$action' is recorded but not listed in AuditActions.All."; $lintError = $true }
foreach ($action in $stale) { Write-Host "Audit actions: '$action' is listed in AuditActions.All but no longer recorded."; $lintError = $true }

$viewerReads = '\b(?<name>GetApprovalAsync|GetApprovalsAsync|GetConsolidatedAsync|GetForSystemAsync|Threads\.ListAsync|threads\.ListAsync)\(\s*(?<first>[^,)\s]+)'
$apiReads = @(Get-ChildItem src/DemaConsulting.ReviewCenter/Api -Recurse -Filter *.cs | ForEach-Object {
        $file = $_.Name
        [regex]::Matches((Get-Content $_.FullName -Raw), $viewerReads) |
            ForEach-Object { [pscustomobject]@{ File = $file; Name = $_.Groups['name'].Value; First = $_.Groups['first'].Value } }
    })
if ($apiReads.Count -lt 3) {
    Write-Host "API reads: the scan found too few reads to be trusted; the pattern is out of date."
    $lintError = $true
}
foreach ($read in $apiReads | Where-Object First -cne 'actor') {
    Write-Host "API reads: $($read.File) calls $($read.Name) without the actor."
    $lintError = $true
}
Get-ChildItem src -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '[/\\](bin|obj)[/\\]' } |
    Where-Object { (Get-Content $_.FullName -Raw).Contains('GetForSystemAsync') } |
    ForEach-Object { Write-Host "API reads: $($_.Name) uses the removed viewer-less read GetForSystemAsync."; $lintError = $true }

# --- Compliance Tools ---
Write-Host "Linting: compliance tools..."
if (-not $skipDotnetTools) {
    dotnet reqstream --lint --enforce --requirements requirements.yaml
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    dotnet versionmark --lint
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    dotnet reviewmark --lint
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    if (Test-Path docs/sysml2) {
        dotnet sysml2tools lint --strict-unresolved 'docs/sysml2/**/*.sysml'
        if ($LASTEXITCODE -ne 0) { $lintError = $true }
    }
}

# [PROJECT-SPECIFIC] Add additional dotnet tool checks here.

# --- dotnet Format ---
Write-Host "Linting: dotnet format..."
$skipDotnetFormat = $false
dotnet restore > $null
if ($LASTEXITCODE -ne 0) { $lintError = $true; $skipDotnetFormat = $true }

if (-not $skipDotnetFormat) {
    dotnet format --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { $lintError = $true }
}

# [PROJECT-SPECIFIC] C/C++ clang-format check example:
#   Get-ChildItem -Recurse -Include "*.cpp","*.hpp","*.h","*.c" |
#       Where-Object { $_.FullName -notmatch '[/\\](thirdparty|third-party|3rd-party|generated)[/\\]' } |
#       ForEach-Object {
#           $result = clang-format --dry-run --Werror $_.FullName 2>&1
#           if ($LASTEXITCODE -ne 0) { Write-Output $result; $lintError = $true }
#       }

exit ($lintError ? 1 : 0)
