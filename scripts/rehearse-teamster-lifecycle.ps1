<#
.SYNOPSIS
CT-043: rehearses the Teamster release-tag chain's integrity, a fresh
install, an upgrade across the one real config-schema boundary (v0.7.0 ->
current), and a clean uninstall -- entirely inside a scratch directory
under artifacts/, never touching a real mod-manager profile.

.DESCRIPTION
This proves what is safely provable without a running game:
  1. Every sealed concerned-teamster/vX.Y.Z tag's csproj Version matches
     its tag name and versions strictly increase (the full "chain").
  2. A fresh install of the CURRENT source's real packaged ZIP produces
     the expected single-DLL plugins/ layout.
  3. Upgrading from a real, actually-built v0.7.0 package (the last
     version before CT-039 introduced config schema versioning) to the
     current source's real package replaces the DLL and leaves no stale
     file behind.
  4. Uninstalling (deleting the plugin file) leaves zero lingering
     Teamster references anywhere under the scratch BepInEx tree.
  5. The Copy-Item -Force primitive deploy.ps1 uses to populate a real
     profile is idempotent (run twice, identical bytes both times).

What this does NOT and cannot prove: that BepInEx actually invokes
Plugin.Awake's migration correctly during a real Valheim launch, or that
a real mod-manager profile behaves identically to this scratch
simulation. Those stay pending, owner-run, in-game checks -- see
HUMAN_ATTENTION.md's CT-043 entry. The migration LOGIC itself
(ConfigSchemaMigration.Decide, TripPersistPlan.Decide) is already
exhaustively unit-tested against realistic fixtures in
ConcernedTeamster.Tests; this script proves the FILE-level mechanics
around it, not a duplicate of that logic coverage.
#>
[CmdletBinding()]
param(
    [switch]$KeepScratch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "RepoTools.psm1") -Force

$root = Get-RepoRoot
Assert-Command python
Assert-Command tcli
Assert-Command git

$dllName = "TheConcernedCat.ConcernedTeamster.dll"
$scratchRoot = Join-Path $root "artifacts\teamster-rehearsal"
$pluginsDir = Join-Path $scratchRoot "BepInEx\plugins"

function Reset-ScratchPlugins {
    if (Test-Path $pluginsDir) {
        Remove-Item $pluginsDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null
}

function Install-TeamsterZip {
    param([Parameter(Mandatory)][string]$ZipPath)

    $extractDir = Join-Path $env:TEMP ("teamster-rehearsal-extract-" + [guid]::NewGuid().ToString("N"))
    try {
        Expand-Archive -Path $ZipPath -DestinationPath $extractDir -Force
        $extractedDll = Join-Path $extractDir "plugins\$dllName"
        if (-not (Test-Path $extractedDll)) {
            throw "Package $ZipPath has no plugins\$dllName -- cannot simulate an install from it."
        }

        Copy-Item $extractedDll (Join-Path $pluginsDir $dllName) -Force
    }
    finally {
        Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Get-DllHash {
    (Get-FileHash (Join-Path $pluginsDir $dllName) -Algorithm SHA256).Hash
}

Write-Host "=== 1. Release-tag chain integrity ==="
$tagLines = git tag -l "concerned-teamster/v*"
if (-not $tagLines) {
    throw "No concerned-teamster/v* tags found -- nothing to rehearse a chain over."
}

$tags = $tagLines | ForEach-Object {
    [pscustomobject]@{ Tag = $_; Version = [version]($_ -replace 'concerned-teamster/v', '') }
} | Sort-Object Version

$previous = $null
foreach ($entry in $tags) {
    $versionStr = $entry.Version.ToString()
    # .NET [version] drops a trailing ".0" build segment (e.g. 5.0 -> "5.0"
    # stays but 5.0.0 -> "5.0.0"); compare against the exact tag suffix
    # instead of the parsed object's ToString to avoid a false mismatch.
    $expectedVersionStr = $entry.Tag -replace 'concerned-teamster/v', ''
    # git show returns one array element per line in PowerShell -- join
    # before matching, since -match/-notmatch on an array has per-element
    # (filter) semantics, not "does this substring appear anywhere".
    $csprojText = (git show "$($entry.Tag):src/ConcernedTeamster/ConcernedTeamster.csproj") -join "`n"
    if ($csprojText -notmatch [regex]::Escape("<Version>$expectedVersionStr</Version>")) {
        throw "Chain integrity FAILED: $($entry.Tag)'s csproj Version does not read $expectedVersionStr."
    }

    if ($null -ne $previous -and $entry.Version -le $previous) {
        throw "Chain integrity FAILED: $($entry.Tag) ($($entry.Version)) does not exceed the previous tag's version ($previous)."
    }

    Write-Host "  $($entry.Tag) -- csproj Version matches, exceeds previous"
    $previous = $entry.Version
}
Write-Host "Chain integrity OK: $($tags.Count) sealed versions, strictly increasing, each independently verified against its own tagged commit."

Write-Host "`n=== 2. Fresh install rehearsal (current source) ==="
Reset-ScratchPlugins
& (Join-Path $PSScriptRoot "package.ps1") -Product ConcernedTeamster | Out-Null
$currentZip = Get-ChildItem (Join-Path $root "artifacts\thunderstore") -Filter "TheConcernedCat-ConcernedTeamster-*.zip" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $currentZip) {
    throw "Fresh install rehearsal FAILED: no packaged ZIP found under artifacts\thunderstore after package.ps1."
}

Install-TeamsterZip -ZipPath $currentZip.FullName
$freshHash = Get-DllHash
$freshFileCount = @(Get-ChildItem $pluginsDir -Recurse -File).Count
if ($freshFileCount -ne 1) {
    throw "Fresh install rehearsal FAILED: expected exactly 1 file under scratch plugins/, found $freshFileCount."
}
Write-Host "Fresh install OK: $($currentZip.Name) -> $dllName present alone under plugins/, SHA-256 $freshHash"

Write-Host "`n=== 3. Upgrade rehearsal (v0.7.0, the last pre-schema-versioning release -> current) ==="
$preMigrationTag = "concerned-teamster/v0.7.0"
$worktreePath = Join-Path $env:TEMP ("teamster-rehearsal-worktree-" + [guid]::NewGuid().ToString("N"))
git worktree add --detach $worktreePath $preMigrationTag | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Upgrade rehearsal FAILED: 'git worktree add' for $preMigrationTag did not succeed (exit $LASTEXITCODE) -- nothing was built or copied."
}
try {
    Copy-Item (Join-Path $root "Environment.props") (Join-Path $worktreePath "Environment.props") -Force
    Push-Location $worktreePath
    try {
        & (Join-Path $worktreePath "scripts\package.ps1") -Product ConcernedTeamster | Out-Null
    }
    finally {
        Pop-Location
    }

    $oldZip = Get-ChildItem (Join-Path $worktreePath "artifacts\thunderstore") -Filter "TheConcernedCat-ConcernedTeamster-0.7.0.zip"
    if (-not $oldZip) {
        throw "Upgrade rehearsal FAILED: could not build a real v0.7.0 package from $preMigrationTag."
    }

    Reset-ScratchPlugins
    Install-TeamsterZip -ZipPath $oldZip.FullName
    $oldHash = Get-DllHash
    Write-Host "Simulated a real v0.7.0 install: SHA-256 $oldHash"

    Install-TeamsterZip -ZipPath $currentZip.FullName
    $newHash = Get-DllHash
    if ($newHash -eq $oldHash) {
        throw "Upgrade rehearsal FAILED: DLL hash did not change after overlaying the current build over v0.7.0 -- the upgrade did not actually replace anything."
    }

    $upgradedFileCount = @(Get-ChildItem $pluginsDir -Recurse -File).Count
    if ($upgradedFileCount -ne 1) {
        throw "Upgrade rehearsal FAILED: expected exactly 1 file under scratch plugins/ after upgrade (no stale file left behind), found $upgradedFileCount."
    }

    Write-Host "Upgrade OK: DLL replaced (v0.7.0 $oldHash -> current $newHash), exactly 1 file remains under plugins/ (no stale v0.7.0 artifact left behind)."
}
finally {
    Pop-Location -ErrorAction SilentlyContinue
    # CT-043 review finding: a worktree killed while still in git's own
    # transient "locked: initializing" state (e.g. the whole process tree
    # hard-killed mid-run) needs a DOUBLE --force to actually remove --
    # a single --force silently no-ops on a locked worktree, and even
    # `git worktree prune` alone skips locked entries too. Empirically
    # reproduced and confirmed by hard-killing this exact step; recovery
    # needed exactly this double-force. The plain Remove-Item and trailing
    # prune below are a backstop, not the primary fix.
    git worktree remove $worktreePath --force --force 2>$null
    git worktree prune -f 2>$null
    Remove-Item $worktreePath -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`nNote: this proves the FILE-level upgrade mechanics using two real, actually-built packages. It does not and cannot prove BepInEx invokes Plugin.Awake's config/sidecar migration correctly during a real Valheim launch -- that needs a live game session (pending, HUMAN_ATTENTION.md). The migration LOGIC itself is already exhaustively unit-tested against realistic fixtures (ConfigSchemaMigrationTests, TripPersistPlanTests, TripPersistenceTests)."

Write-Host "`n=== 4. Uninstall rehearsal ==="
Remove-Item (Join-Path $pluginsDir $dllName) -Force
$remainingFiles = Get-ChildItem $scratchRoot -Recurse -File -ErrorAction SilentlyContinue
$teamsterReferences = $remainingFiles | Where-Object {
    (Select-String -Path $_.FullName -Pattern "ConcernedTeamster" -SimpleMatch -Quiet -ErrorAction SilentlyContinue)
}
if ($teamsterReferences) {
    throw "Uninstall rehearsal FAILED: found lingering Teamster references in: $($teamsterReferences.FullName -join ', ')"
}
Write-Host "Uninstall OK: the plugin DLL removed; zero lingering Teamster references anywhere under the scratch BepInEx tree."
Write-Host "Note: this only rehearses removing the PLUGIN itself. A player's own trip-history sidecar and any exported support bundles live separately under BepInEx/config/ConcernedCatMods/ConcernedTeamster/ and are untouched by removing the plugin -- deleting that folder too is the player's own separate choice, documented in the package README."

Write-Host "`n=== 5. Deploy-copy idempotence (the primitive deploy.ps1 uses) ==="
$idempotenceDir = Join-Path $env:TEMP ("teamster-rehearsal-idempotence-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $idempotenceDir -Force | Out-Null
try {
    $sourceDll = Join-Path $root "src\ConcernedTeamster\bin\Release\net48\$dllName"
    if (-not (Test-Path $sourceDll)) {
        throw "Idempotence check FAILED: expected the just-built Release DLL at $sourceDll."
    }

    Copy-Item $sourceDll (Join-Path $idempotenceDir $dllName) -Force
    $firstRunHash = (Get-FileHash (Join-Path $idempotenceDir $dllName) -Algorithm SHA256).Hash
    Copy-Item $sourceDll (Join-Path $idempotenceDir $dllName) -Force
    $secondRunHash = (Get-FileHash (Join-Path $idempotenceDir $dllName) -Algorithm SHA256).Hash

    if ($firstRunHash -ne $secondRunHash) {
        throw "Idempotence check FAILED: two identical Copy-Item -Force runs produced different bytes ($firstRunHash vs $secondRunHash)."
    }

    Write-Host "Idempotence OK: two Copy-Item -Force runs of the same source produced identical bytes (SHA-256 $firstRunHash both times) -- deploy.ps1's copy primitive changes nothing on a second run."
}
finally {
    Remove-Item $idempotenceDir -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not $KeepScratch) {
    Remove-Item $scratchRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`nCT-043 lifecycle rehearsal complete. Nothing outside artifacts\ and system temp was touched; no real mod-manager profile was created, modified, or read."
