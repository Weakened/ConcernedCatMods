[CmdletBinding()]
param()

# Issue #258 live game-API audit: proves, against the INSTALLED Valheim
# assemblies and asset catalog, that dungeon entrances are world
# locations rather than networked objects, that the loaded-location
# surface the survey now reads still exists, and that the shipped starter
# rules cover every installed dungeon-entrance location identity.
#
# Static audit only. It reads the installed game; it does not launch it,
# and it proves nothing about in-game behaviour.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "RepoTools.psm1") -Force

$root = Get-RepoRoot
$environment = Get-EnvironmentValues -Root $root
Assert-Command ilspycmd
Assert-PathValue -Name "VALHEIM_INSTALL" -Path $environment.ValheimInstall
Assert-PathValue -Name "BEPINEX_PATH" -Path $environment.BepInExPath

$managed = Join-Path $environment.ValheimInstall "valheim_Data\Managed"
$gameAssembly = Join-Path $managed "assembly_valheim.dll"
$softRefManifest = Join-Path $environment.ValheimInstall "valheim_Data\StreamingAssets\SoftRef\manifest"
$globalManagers = Join-Path $environment.ValheimInstall "valheim_Data\globalgamemanagers"
$bepInExDll = Join-Path $environment.BepInExPath "core\BepInEx.dll"
$jotunnDll = Join-Path $environment.BepInExPath "plugins\ValheimModding-Jotunn\Jotunn.dll"
$ruleSource = Join-Path $root "src\ConcernedCartographer\Domain\Atlas\SurveyRuleSet.cs"
foreach ($required in @($gameAssembly, $softRefManifest, $globalManagers, $bepInExDll, $jotunnDll, $ruleSource)) {
    if (-not (Test-Path $required -PathType Leaf)) {
        throw "Required audit input is missing: $required"
    }
}

function Get-TypeSource {
    param([Parameter(Mandatory)][string]$TypeName)

    $source = (& ilspycmd -t $TypeName $gameAssembly | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect Valheim type $TypeName."
    }

    return [regex]::Replace($source, "\s+", " ").Trim()
}

function Assert-SourceContains {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Needle,
        [Parameter(Mandatory)][string]$Contract
    )

    if (-not $Source.Contains($Needle, [StringComparison]::Ordinal)) {
        throw "Valheim survey contract moved: $Contract"
    }
}

function Assert-SourceOrder {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string[]]$Needles,
        [Parameter(Mandatory)][string]$Contract
    )

    $cursor = 0
    foreach ($needle in $Needles) {
        $index = $Source.IndexOf($needle, $cursor, [StringComparison]::Ordinal)
        if ($index -lt 0) {
            throw "Valheim survey contract moved: $Contract (missing or out of order: $needle)"
        }

        $cursor = $index + $needle.Length
    }
}

function Get-FileIdentity {
    param([Parameter(Mandatory)][string]$Path)

    $item = Get-Item $Path
    $assemblyVersion = if ($item.Extension -eq ".dll") {
        [Reflection.AssemblyName]::GetAssemblyName($Path).Version.ToString()
    } else {
        $null
    }

    [ordered]@{
        file = $item.Name
        bytes = $item.Length
        sha256 = (Get-FileHash $Path -Algorithm SHA256).Hash
        assemblyVersion = $assemblyVersion
    }
}

$versionSource = Get-TypeSource -TypeName "Version"
$versionMatch = [regex]::Match(
    $versionSource,
    'CurrentVersion\s*\{\s*get;\s*\}\s*=\s*new GameVersion\((\d+),\s*(\d+),\s*(\d+)\)'
)
if (-not $versionMatch.Success) {
    throw "Could not resolve Valheim GameVersion from assembly_valheim.dll."
}
$gameVersion = '{0}.{1}.{2}' -f $versionMatch.Groups[1].Value,
    $versionMatch.Groups[2].Value, $versionMatch.Groups[3].Value

$managerText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($globalManagers))
$unityMatch = [regex]::Match($managerText, '6000\.0\.\d+f\d+')
if (-not $unityMatch.Success) {
    throw "Could not resolve the Unity 6000 version."
}

# --- 1. The loaded-location surface the survey reads ------------------------
$location = Get-TypeSource -TypeName "Location"
Assert-SourceContains $location 'private static List<Location> s_allLocations = new List<Location>();' `
    "Location.s_allLocations is the loaded-location surface"
# Contiguous, not merely ordered: an ordered assertion would still pass if
# the registration moved into any later method.
Assert-SourceContains $location 'private void Awake() { s_allLocations.Add(this);' `
    "loaded locations must be registered in Location.Awake"
Assert-SourceContains $location 'private void OnDestroy() { s_allLocations.Remove(this); }' `
    "loaded locations must be unregistered in Location.OnDestroy"
Assert-SourceContains $location 'public float m_exteriorRadius = 20f;' `
    "a location publishes its own exterior radius"

# --- 2. Why the networked surface alone can never see a dungeon -------------
$locationProxy = Get-TypeSource -TypeName "LocationProxy"
Assert-SourceContains $locationProxy 'm_nview = GetComponent<ZNetView>();' `
    "the networked stand-in for a location is LocationProxy, not the location"
Assert-SourceContains $locationProxy 'm_instance = ZoneSystem.instance.SpawnProxyLocation(num, seed, base.transform.position, base.transform.rotation);' `
    "LocationProxy spawns the real location prefab as its child"

$zoneSystem = Get-TypeSource -TypeName "ZoneSystem"
Assert-SourceContains $zoneSystem 'return SpawnLocation(location, seed, pos, rot, SpawnMode.Client, spawnedGhostObjects);' `
    "client-side location spawning uses SpawnMode.Client"
# ONE contiguous substring. The loop header alone appears in both the
# Full/Ghost and the Client branch, so an ordered assertion would prove
# nothing about the branch this fix depends on.
Assert-SourceContains $zoneSystem ('for (int j = 0; j < array3.Length; j++) ' +
    '{ array3[j].gameObject.SetActive(value: false); } ' +
    'gameObject = SoftReferenceableAssets.Utils.Instantiate(location.m_prefab, pos, rot);') `
    "SpawnMode.Client must deactivate every location ZNetView immediately before instantiating the prefab, which is why no location-named object is ever registered in ZNetScene.m_instances"

$zNetScene = Get-TypeSource -TypeName "ZNetScene"
Assert-SourceContains $zNetScene 'private readonly Dictionary<ZDO, ZNetView> m_instances' `
    "ZNetScene.m_instances remains the networked-object surface"

# --- 3. The installed location catalog --------------------------------------
$manifestBytes = [IO.File]::ReadAllBytes($softRefManifest)
$manifestText = [Text.Encoding]::ASCII.GetString($manifestBytes)
$catalog = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($match in [regex]::Matches($manifestText, 'Assets/world/Locations/[^\s]*?/([A-Za-z0-9_\-]+)\.prefab')) {
    [void]$catalog.Add($match.Groups[1].Value)
}
if ($catalog.Count -lt 100) {
    throw "Installed location catalog looks wrong: only $($catalog.Count) entries resolved."
}

# Reviewed dungeon-entrance locations. Every one must exist in the installed
# catalog AND be matched by a shipped Dungeons rule. The first three are the
# identities named in issue #258.
$dungeonLocations = @(
    "Crypt2", "Crypt3", "Crypt4",
    "TrollCave02",
    "BearCave",
    "HalfBurried_ForestCrypt",
    "Hildir_crypt",
    "Hildir_cave",
    "MountainCave02",
    "SunkenCrypt4"
)
$missingFromCatalog = @($dungeonLocations | Where-Object { -not $catalog.Contains($_) })
if ($missingFromCatalog.Count -gt 0) {
    throw "Installed location catalog no longer contains: $($missingFromCatalog -join ', ')"
}

# --- 4. The shipped starter rules cover every one of them -------------------
$ruleText = Get-Content -LiteralPath $ruleSource -Raw
$defaultBody = [regex]::Match(
    $ruleText,
    'public static SurveyRuleSet Default\(\)\s*\{(?<body>.*?)\n    \}',
    [Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $defaultBody.Success) {
    throw "Could not locate SurveyRuleSet.Default() in $ruleSource."
}

$dungeonPatterns = @()
foreach ($match in [regex]::Matches($defaultBody.Groups['body'].Value,
    'new SurveyRule\("(?<pattern>[^"]+)",\s*"cc:dungeon"')) {
    $dungeonPatterns += $match.Groups['pattern'].Value
}
if ($dungeonPatterns.Count -eq 0) {
    throw "SurveyRuleSet.Default() ships no cc:dungeon rules."
}

$unmatched = @()
foreach ($dungeon in $dungeonLocations) {
    $clean = $dungeon.ToLowerInvariant()
    $matched = $false
    foreach ($pattern in $dungeonPatterns) {
        $cleanPattern = $pattern.ToLowerInvariant()
        if ($cleanPattern.EndsWith("*")) {
            if ($clean.StartsWith($cleanPattern.Substring(0, $cleanPattern.Length - 1), [StringComparison]::Ordinal)) {
                $matched = $true
                break
            }
        } elseif ($clean -eq $cleanPattern) {
            $matched = $true
            break
        }
    }

    if (-not $matched) {
        $unmatched += $dungeon
    }
}
if ($unmatched.Count -gt 0) {
    throw "Shipped starter rules do not cover installed dungeon locations: $($unmatched -join ', ')"
}

# Report, do not assert, the installed locations no shipped rule covers.
# The audited list above is a reviewed scope; this makes the REST of the
# catalog visible instead of letting a self-selected list imply coverage.
$ruleCoveredLocations = @()
$uncoveredLocations = @()
foreach ($candidate in ($catalog | Sort-Object)) {
    $clean = $candidate.ToLowerInvariant()
    $isCovered = $false
    foreach ($pattern in $dungeonPatterns) {
        $cleanPattern = $pattern.ToLowerInvariant()
        if ($cleanPattern.EndsWith("*")) {
            if ($clean.StartsWith($cleanPattern.Substring(0, $cleanPattern.Length - 1), [StringComparison]::Ordinal)) {
                $isCovered = $true
                break
            }
        } elseif ($clean -eq $cleanPattern) {
            $isCovered = $true
            break
        }
    }

    if ($isCovered) { $ruleCoveredLocations += $candidate } else { $uncoveredLocations += $candidate }
}

# The runestone pair the loaded-location surface newly exposes: the same
# site exists both as a networked prop prefab and as a world location.
# Both spellings are asserted so the duplicate-collapse behaviour rests on
# evidence rather than on an assumed name.
$extendedManifest = Join-Path $environment.ValheimInstall "valheim_Data\StreamingAssets\SoftRef\manifest_extended"
if (-not (Test-Path $extendedManifest -PathType Leaf)) {
    throw "Required audit input is missing: $extendedManifest"
}
$extendedText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($extendedManifest))
if (-not $extendedText.Contains('Assets/world/Props/RuneStones/RuneStone_BlackForest.prefab', [StringComparison]::Ordinal)) {
    throw "Installed catalog no longer has the networked RuneStone_BlackForest prop."
}
if (-not $catalog.Contains('Runestone_BlackForest')) {
    throw "Installed catalog no longer has the Runestone_BlackForest location."
}

$result = [ordered]@{
    result = "PASS"
    scope = "static audit of installed assemblies and asset catalog; NOT live in-game evidence"
    valheimVersion = $gameVersion
    unityVersion = $unityMatch.Value
    loadedLocationSurface = "Location.s_allLocations (private static List<Location>; Awake adds, OnDestroy removes)"
    networkedSurface = "ZNetScene.m_instances (Dictionary<ZDO, ZNetView>)"
    whyDungeonsWereMissed = "LocationProxy is the only networked object at a location; SpawnMode.Client deactivates every location ZNetView before instantiating the prefab, so no location-named object is ever registered"
    locationCatalogEntries = $catalog.Count
    auditedDungeonLocations = $dungeonLocations
    shippedDungeonRulePatterns = $dungeonPatterns
    locationsCoveredByDungeonRules = $ruleCoveredLocations
    locationsNotCoveredByDungeonRules = $uncoveredLocations.Count
    runestoneDualSurface = "RuneStone_BlackForest (networked prop) and Runestone_BlackForest (world location) both present; identities collapse through the rule duplicate radius"
    gameAssembly = Get-FileIdentity $gameAssembly
    softRefManifest = Get-FileIdentity $softRefManifest
    globalManagers = Get-FileIdentity $globalManagers
    bepinex = Get-FileIdentity $bepInExDll
    jotunn = Get-FileIdentity $jotunnDll
}

$result | ConvertTo-Json -Depth 5
Write-Host "Cartographer survey location-surface audit PASS."
