<#
.SYNOPSIS
    The one gate. Builds every product, runs every test, validates the repo, and
    prints a block a PR body can paste verbatim.

.DESCRIPTION
    This exists because "the full suite is green" was true three times in this
    repository over work that was broken, and once over an assembly that did not
    compile at all (#360, #284/PR #354 at 694a6e3).

    The trap is specific and quiet: `dotnet test <sln>` does NOT build non-test
    product projects. A solution-wide test run can pass while ConcernedForeman,
    ConcernedCartographer, ConcernedTeamster and ConcernedSteward are all
    uncompilable, because nothing asked for them. CI cannot cover the gap either
    - the products reference the licensed game assemblies, which no runner has -
    so the build gate can only exist here, on a developer machine.

    Hence: build first, test with --no-build second. The order is the point. A
    test step that is allowed to build its own way around a failed product build
    would restore exactly the hole this script closes.

.PARAMETER Configuration
    Release by default, because Release is what ships and what evidence should
    quote. Debug is for iterating.

.PARAMETER SkipTests
    Build and validate only. For a quick compile check; never for evidence.

.EXAMPLE
    pwsh ./scripts/verify.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "RepoTools.psm1") -Force

$root = Get-RepoRoot
Assert-Command dotnet

# Refuse rather than degrade. A verify that quietly skips the product build on a
# machine without the game is a verify that reports the same green as the run
# that let a red `main` through, which is worse than no script: it launders the
# absence of a check into the appearance of one.
$environment = Get-EnvironmentValues -Root $root
Assert-PathValue -Name "VALHEIM_INSTALL" -Path $environment.ValheimInstall
Assert-PathValue -Name "BEPINEX_PATH" -Path $environment.BepInExPath

$solution = Join-Path $root "ConcernedCatMods.sln"
$started = Get-Date
$steps = [System.Collections.Generic.List[object]]::new()

function Add-Step {
    param([string]$Name, [string]$Detail)
    $steps.Add([pscustomobject]@{ Name = $Name; Detail = $Detail })
}

Push-Location $root
try {
    # --- 1. Every project in the solution, products included -----------------
    Write-Host "[verify] Building the solution ($Configuration). This is the step dotnet test skips." -ForegroundColor Cyan
    $buildLog = & dotnet build $solution --configuration $Configuration --nologo -v m 2>&1
    $buildLog | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw "Solution build FAILED. Nothing below this line ran, and no test result from this tree means anything."
    }

    $projects = (Select-String -InputObject ($buildLog -join "`n") -Pattern '-> .+\.dll' -AllMatches).Matches.Count
    Add-Step "Solution build" "$Configuration, succeeded, $projects assemblies produced"

    # --- 2. Every test, against exactly those assemblies ---------------------
    if ($SkipTests) {
        Add-Step "Tests" "SKIPPED (-SkipTests). This run is not evidence."
    }
    else {
        Write-Host "[verify] Running every test against the assemblies just built." -ForegroundColor Cyan
        # --no-build is load-bearing: it guarantees these are the assemblies the
        # step above produced, and stops a test run from building its own way
        # around a product that does not compile.
        $testLog = & dotnet test $solution --configuration $Configuration --no-build --nologo 2>&1
        $testLog | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) {
            throw "Tests FAILED."
        }

        $passed = 0
        $failed = 0
        foreach ($match in (Select-String -InputObject ($testLog -join "`n") `
                    -Pattern 'Failed:\s+(\d+),\s+Passed:\s+(\d+)' -AllMatches).Matches) {
            $failed += [int]$match.Groups[1].Value
            $passed += [int]$match.Groups[2].Value
        }

        Add-Step "Tests" "$passed passed, $failed failed"
    }

    # --- 3. Everything a compiler cannot see ---------------------------------
    Write-Host "[verify] Validating the repository." -ForegroundColor Cyan
    $python = if (Get-Command python -ErrorAction SilentlyContinue) { "python" } else { "python3" }
    & $python (Join-Path $root "tools/validate_repo.py")
    if ($LASTEXITCODE -ne 0) {
        throw "Repository validation FAILED."
    }

    Add-Step "Validator" "tools/validate_repo.py, exit 0"
}
finally {
    Pop-Location
}

# --- The block a PR body can paste ------------------------------------------
$elapsed = [int]((Get-Date) - $started).TotalSeconds
$commit = (& git -C $root rev-parse --short HEAD 2>$null)
if ($LASTEXITCODE -ne 0) { $commit = "unknown" }

Write-Host ""
Write-Host "================ verify: PASSED ================" -ForegroundColor Green
Write-Host "commit      $commit"
foreach ($step in $steps) {
    Write-Host ("{0,-16}{1}" -f $step.Name, $step.Detail)
}
Write-Host "elapsed     ${elapsed}s"
Write-Host "===============================================" -ForegroundColor Green
