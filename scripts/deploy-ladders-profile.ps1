#requires -Version 7.0
<#
.SYNOPSIS
    Deploy only a locally verified Concerned Ladders DLL to TCC-Ladders-Dev.
.DESCRIPTION
    CL-003. Import the dependency-only r2z as a NEW profile first. This script
    never creates a manager profile, edits mods.yml, downloads a dependency,
    changes a save, launches Valheim, or calls the protected screenshot script.
    A successful deployment is DEPLOYED_NOT_LAUNCHED, never a gameplay PASS.
    Capture scripts/verify.ps1 output after committing the exact source. Supply
    that commit and the hash of the DLL produced by that run. -WhatIf performs
    every read-only preflight and writes nothing. An existing different candidate
    requires -ReplaceExisting and is backed up before its replacement.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "Medium")]
param(
    [Parameter(Mandatory)][string]$ProfilePath,
    [Parameter(Mandatory)][string]$VerifyLog,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$ExpectedSourceCommit,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExpectedDllSha256,
    [switch]$ReplaceExisting
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if (-not $IsWindows) { throw "Actual profile deployment requires the Windows Valheim development machine." }
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$product = "ConcernedLadders"
$dllName = "TheConcernedCat.ConcernedLadders.dll"
$profileName = "TCC-Ladders-Dev"

function Assert-NoLinks([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrEmpty($cursor)) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "A profile or candidate path contains a junction/symbolic link; use the real isolated path."
            }
        }
        $parent = Split-Path -Parent $cursor
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
}

function Assert-GameStopped {
    if (@(Get-Process -Name "valheim", "valheim_server" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Valheim is running. Exit the test game normally before deploying; this script will not stop it."
    }
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-Assembly([string]$Path, [string]$Name) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required assembly is absent: $Name" }
    $identity = [Reflection.AssemblyName]::GetAssemblyName($Path)
    if ($identity.Name -ne $Name) { throw "Unexpected assembly identity at the $Name path." }
    return $identity
}

function Assert-ThreePartVersion([version]$Actual, [version]$Expected, [string]$Name) {
    if ($Actual.Major -ne $Expected.Major -or $Actual.Minor -ne $Expected.Minor -or $Actual.Build -ne $Expected.Build) {
        throw "$Name assembly version $Actual does not match the expected $Expected candidate."
    }
}

Assert-GameStopped
$profile = [IO.Path]::GetFullPath($ProfilePath).TrimEnd([char[]]@('\', '/'))
$profilesDirectory = Split-Path -Parent $profile
if ((Split-Path -Leaf $profile) -ne $profileName -or
    (Split-Path -Leaf $profilesDirectory) -ne "profiles" -or
    (Split-Path -Leaf (Split-Path -Parent $profilesDirectory)) -ne "Valheim") {
    throw "Use the existing manager-created Valheim\profiles\TCC-Ladders-Dev directory, never another profile."
}
Assert-NoLinks $profile
if (-not (Test-Path -LiteralPath (Join-Path $profile "mods.yml") -PathType Leaf)) {
    throw "This is not an imported manager profile. Import the dependency-only r2z as a new profile first."
}

foreach ($command in @("git", "python")) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Required command is missing: $command" }
}
$commit = (& git -C $root rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne $ExpectedSourceCommit) { throw "Current source is not the expected full commit." }
$changes = @(& git -C $root status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0 -or $changes.Count -gt 0) { throw "Commit the exact source before verifying and deploying; the checkout must be clean." }
$contractJson = (& python (Join-Path $root "tools/prepare_ladders_profile.py") --inspect | Out-String)
if ($LASTEXITCODE -ne 0) { throw "The Concerned Ladders package contract could not be read." }
$contract = $contractJson | ConvertFrom-Json -AsHashtable

$candidate = Join-Path $root "src/$product/bin/Release/net48/$dllName"
Assert-NoLinks $candidate
$identity = Assert-Assembly $candidate "TheConcernedCat.ConcernedLadders"
Assert-ThreePartVersion $identity.Version ([version]$contract.version) $product
$candidateHash = Get-Sha256 $candidate
if ($candidateHash -ne $ExpectedDllSha256.ToLowerInvariant()) { throw "The built DLL does not match the supplied candidate SHA256." }

Assert-NoLinks $VerifyLog
$log = Get-Content -LiteralPath $VerifyLog -Raw
$plainLog = [regex]::Replace($log, '\x1B\[[0-?]*[ -/]*[@-~]', '')
$summaries = [regex]::Matches($plainLog, '(?ms)^=+ verify: PASSED =+\s*\r?\n(?<body>.*?)^=+\s*$')
if ($summaries.Count -ne 1) { throw "VerifyLog must contain exactly one successful scripts/verify.ps1 summary." }
$summary = $summaries[0].Groups['body'].Value
$commitLine = [regex]::Match($summary, '(?m)^commit\s+([0-9a-fA-F]{7,40})\s*$')
if (-not $commitLine.Success -or -not $commit.StartsWith($commitLine.Groups[1].Value, [StringComparison]::OrdinalIgnoreCase)) {
    throw "verify.ps1 evidence belongs to a different source commit."
}
foreach ($requiredLine in @(
    '(?m)^Solution build\s+Release, succeeded, [1-9]\d* assemblies produced\s*$',
    '(?m)^Tests\s+[1-9]\d* passed, 0 failed\s*$',
    '(?m)^Validator\s+tools/validate_repo\.py, exit 0\s*$'
)) {
    if (-not [regex]::IsMatch($summary, $requiredLine)) { throw "verify.ps1 evidence lacks the full Release build, tests, or validator pass; -SkipTests is insufficient." }
}
if ((Get-Item -LiteralPath $VerifyLog).LastWriteTimeUtc -lt (Get-Item -LiteralPath $candidate).LastWriteTimeUtc) {
    throw "The DLL was modified after the supplied verification log; verify this candidate again."
}

$plugins = Join-Path $profile "BepInEx/plugins"
$bepIdentity = Assert-Assembly (Join-Path $profile "BepInEx/core/BepInEx.dll") "BepInEx"
if ($bepIdentity.Version.Major -ne 5) { throw "This product requires the Valheim BepInEx 5 loader pack." }
$profileItems = @(Get-ChildItem -LiteralPath (Join-Path $profile "BepInEx") -Force -Recurse)
if (@($profileItems | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }).Count -gt 0) {
    throw "The isolated BepInEx profile must not contain junctions or symbolic links."
}
$installedDlls = @(Get-ChildItem -LiteralPath $plugins -File -Filter "*.dll" -Recurse)
$jotunnDlls = @($installedDlls | Where-Object Name -eq "Jotunn.dll")
if ($jotunnDlls.Count -ne 1) { throw "Expected exactly one installed Jotunn.dll; finish dependency import or resolve duplicates first." }
$jotunnIdentity = Assert-Assembly $jotunnDlls[0].FullName "Jotunn"
Assert-ThreePartVersion $jotunnIdentity.Version ([version]$contract.dependencies['ValheimModding-Jotunn']) "Jotunn"
$otherDlls = @($installedDlls | Where-Object { $_.Name -ne "Jotunn.dll" -and $_.Name -ne $dllName })
if ($otherDlls.Count -gt 0) { throw "The clean ladder profile contains other plugin DLLs. Preserve it and import a clean profile before continuing." }

$destinationDirectory = Join-Path $plugins $product
$destination = Join-Path $destinationDirectory $dllName
$ownDlls = @($installedDlls | Where-Object Name -eq $dllName)
if ($ownDlls.Count -gt 1 -or ($ownDlls.Count -eq 1 -and $ownDlls[0].FullName -ne $destination)) {
    throw "A Concerned Ladders DLL is already present at another location. Resolve the duplicate explicitly; no file was removed."
}
$previousHash = $null
if (Test-Path -LiteralPath $destination) {
    $null = Assert-Assembly $destination "TheConcernedCat.ConcernedLadders"
    $previousHash = Get-Sha256 $destination
    if ($previousHash -ne $candidateHash -and -not $ReplaceExisting) {
        throw "A different ladder candidate is installed. Use -ReplaceExisting to preserve a backup and deploy the verified replacement."
    }
}
$receiptPath = Join-Path $profile "ConcernedLadders.deployment.json"
Assert-NoLinks $receiptPath
if (Test-Path -LiteralPath $receiptPath) {
    $oldReceipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json -AsHashtable
    if ($oldReceipt.product -ne $product -or $oldReceipt.schemaVersion -ne 1) {
        throw "The receipt path belongs to an unknown file; it will not be overwritten."
    }
}

$receipt = [ordered]@{
    schemaVersion = 1
    product = $product
    version = $contract.version
    profileName = $profileName
    sourceCommit = $commit
    sourceTreeClean = $true
    dllSha256 = $candidateHash
    previousDllSha256 = $previousHash
    verifyLogSha256 = Get-Sha256 $VerifyLog
    packageManifestSha256 = $contract.manifestSha256
    expectedDependencyPackagePins = $contract.dependencies
    observedDependencyAssemblyVersions = @{ BepInEx = $bepIdentity.Version.ToString(); Jotunn = $jotunnIdentity.Version.ToString() }
    loaderPackagePinVerification = "Confirm the BepInExPack version in the manager; its version differs from the assembly version."
    status = "PLAN_ONLY"
    mutationPerformed = $false
    gameLaunched = $false
    gameplaySmokePassed = $false
    multiplayerTested = $false
}
if (-not $PSCmdlet.ShouldProcess($profile, "Deploy the verified Concerned Ladders DLL and record DEPLOYED_NOT_LAUNCHED")) {
    $receipt | ConvertTo-Json -Depth 6
    return
}

$lockPath = Join-Path $profile ".ConcernedLadders.deploy.lock"
$lock = [IO.File]::Open($lockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
$temporaryDll = Join-Path $destinationDirectory (".candidate-" + [guid]::NewGuid().ToString("N") + ".tmp")
$temporaryReceipt = Join-Path $profile (".receipt-" + [guid]::NewGuid().ToString("N") + ".tmp")
try {
    Assert-GameStopped
    if (Test-Path -LiteralPath $destination) {
        if (-not $previousHash -or (Get-Sha256 $destination) -ne $previousHash) {
            throw "The installed candidate changed after preflight; preserving that file and stopping."
        }
    } elseif ($previousHash) {
        throw "The installed candidate disappeared after preflight; inspect the profile before retrying."
    }
    if ($previousHash -and $previousHash -ne $candidateHash) {
        $backup = Join-Path $root "artifacts/ladders-profile-backups/$previousHash/$dllName"
        Assert-NoLinks $backup
        if (Test-Path -LiteralPath $backup) {
            if ((Get-Sha256 $backup) -ne $previousHash) { throw "An existing candidate backup has unexpected bytes; preserving it." }
        } else {
            New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
            Copy-Item -LiteralPath $destination -Destination $backup
        }
        $receipt['previousCandidateBackup'] = "artifacts/ladders-profile-backups/$previousHash/$dllName"
    }
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $candidate -Destination $temporaryDll
    if ((Get-Sha256 $temporaryDll) -ne $candidateHash) { throw "Staged candidate hash mismatch." }
    [IO.File]::Move($temporaryDll, $destination, $true)
    if ((Get-Sha256 $destination) -ne $candidateHash) { throw "Installed candidate hash mismatch." }
    $receipt.status = "DEPLOYED_NOT_LAUNCHED"
    $receipt.mutationPerformed = $true
    $receipt['deployedAtUtc'] = [DateTime]::UtcNow.ToString("o")
    $receiptJson = $receipt | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText($temporaryReceipt, $receiptJson, [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporaryReceipt, $receiptPath, $true)
    Write-Output $receiptJson
    Write-Host "Deployment recorded. Select TCC-Ladders-Dev and use the manager's Start modded; gameplay remains untested."
} catch {
    throw "Deployment stopped: $($_.Exception.Message) Inspect the installed DLL hash and receipt before retrying; a copy may have completed."
} finally {
    foreach ($temporary in @($temporaryDll, $temporaryReceipt)) {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
    $lock.Dispose()
    Remove-Item -LiteralPath $lockPath
}
