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

# Every run keeps its own full output (#413). A gate that failed once and passed
# on the rerun left nothing to diagnose, because the only copy of the failing
# assembly's summary had scrolled out of a terminal. One folder per run, never
# overwritten, so the first sighting of an intermittent red is enough.
$runCommit = (& git -C $root rev-parse --short HEAD 2>$null)
if ($LASTEXITCODE -ne 0) { $runCommit = "unknown" }
$runDirectory = Join-Path $root ("artifacts/verify/{0:yyyyMMdd-HHmmss}-{1}-{2}" -f $started, $runCommit, $PID)
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
# About 8 MB a run, almost all TRX. Keep the newest 30 runs of this worktree;
# artifacts/ is git-ignored and nothing else is ever written under verify/.
Get-ChildItem -LiteralPath (Split-Path $runDirectory) -Directory |
    Sort-Object Name -Descending | Select-Object -Skip 30 |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host "[verify] This run's full output is kept in $runDirectory" -ForegroundColor Cyan
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
    $buildExit = $LASTEXITCODE
    $buildLog | Set-Content -Path (Join-Path $runDirectory "build.log") -Encoding utf8
    $buildLog | ForEach-Object { Write-Host $_ }
    if ($buildExit -ne 0) {
        throw "Solution build FAILED. Nothing below this line ran, and no test result from this tree means anything. Full log: $runDirectory"
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
        # The TRX files name every test and its outcome per assembly, which is
        # what the console summary alone could not do once it had scrolled away.
        $testResults = Join-Path $runDirectory "TestResults"
        $testLog = & dotnet test $solution --configuration $Configuration --no-build --nologo `
            --logger "trx" --results-directory $testResults 2>&1
        $testExit = $LASTEXITCODE
        $testLog | Set-Content -Path (Join-Path $runDirectory "tests.log") -Encoding utf8
        $testLog | ForEach-Object { Write-Host $_ }
        if ($testExit -ne 0) {
            # Name what failed on the first sighting, from the console lines and
            # from the TRX files, so a rerun is never needed just to find out.
            $failedLines = @($testLog | Where-Object { "$_" -match '^\s*Failed!?\s+\S|\[FAIL\]|error\s+\w*\d+:|aborted|crashed' })
            $failedTrx = @()
            # -LiteralPath: the logger names files "...net10.0[1].trx", and
            # brackets are wildcards to -Path. Reading a TRX is best effort and
            # must never replace the failure being reported with its own error.
            foreach ($trx in @(Get-ChildItem -LiteralPath $testResults -Filter *.trx -Recurse -ErrorAction SilentlyContinue)) {
                try {
                    [xml]$document = Get-Content -Raw -LiteralPath $trx.FullName
                    foreach ($result in @($document.TestRun.Results.UnitTestResult)) {
                        if ($null -ne $result -and $result.outcome -eq "Failed") {
                            $failedTrx += "$($result.testName)  ($($trx.Name))"
                        }
                    }
                }
                catch {
                    $failedTrx += "could not read $($trx.Name): $($_.Exception.GetType().Name)"
                }
            }
            $summary = @($failedTrx + $failedLines | Select-Object -Unique)
            $summary | Set-Content -Path (Join-Path $runDirectory "failures.txt") -Encoding utf8
            Write-Host ""
            Write-Host "[verify] Failing tests ($($failedTrx.Count) from TRX):" -ForegroundColor Red
            $summary | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
            throw "Tests FAILED. Full output, TRX results and failures.txt: $runDirectory"
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
    # Captured rather than printed straight to the console, Python would encode
    # as the ANSI code page: dashes arrive mangled and the first character
    # outside it (an arrow, say) would crash the validator into a false red.
    $priorPythonEncoding = $env:PYTHONIOENCODING
    $priorConsoleEncoding = [Console]::OutputEncoding
    try {
        $env:PYTHONIOENCODING = "utf-8"
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
        $validatorLog = & $python (Join-Path $root "tools/validate_repo.py") 2>&1
    }
    finally {
        $env:PYTHONIOENCODING = $priorPythonEncoding
        [Console]::OutputEncoding = $priorConsoleEncoding
    }
    $validatorExit = $LASTEXITCODE
    $validatorLog | Set-Content -Path (Join-Path $runDirectory "validator.log") -Encoding utf8
    $validatorLog | ForEach-Object { Write-Host $_ }
    if ($validatorExit -ne 0) {
        throw "Repository validation FAILED. Full log: $runDirectory"
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
Write-Host "output      $runDirectory"
Write-Host "===============================================" -ForegroundColor Green
