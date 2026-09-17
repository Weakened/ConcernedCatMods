[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipBuild
)

# Issue #314 (CT-NPC-003) game-API audit for Gunnar's cart navigation adapters
# (src/ConcernedTeamster/Adapters/Navigation). Proves, against the INSTALLED
# Valheim and Unity assemblies, that every game member the adapters compile
# against still has the audited shape and meaning, that the physics layers they
# name exist, and - from the built Teamster DLL's IL - that the adapters use no
# game member outside this audited list.
#
# Static audit only: it reads the installed game and the built DLL. It does not
# launch the game and proves nothing about in-game behaviour. Run it through the
# machine-wide build lock when it builds (without -SkipBuild).

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "RepoTools.psm1") -Force

$root = Get-RepoRoot
$environment = Get-EnvironmentValues -Root $root
Assert-Command ilspycmd
Assert-PathValue -Name "VALHEIM_INSTALL" -Path $environment.ValheimInstall
Assert-PathValue -Name "BEPINEX_PATH" -Path $environment.BepInExPath

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -Product ConcernedTeamster
}

$managed = Join-Path $environment.ValheimInstall "valheim_Data\Managed"
$gameAssembly = Join-Path $managed "assembly_valheim.dll"
$physicsModule = Join-Path $managed "UnityEngine.PhysicsModule.dll"
$coreModule = Join-Path $managed "UnityEngine.CoreModule.dll"
$globalManagers = Join-Path $environment.ValheimInstall "valheim_Data\globalgamemanagers"
$output = Join-Path $root "src\ConcernedTeamster\bin\$Configuration\net48"
$teamsterDll = Join-Path $output "TheConcernedCat.ConcernedTeamster.dll"
foreach ($required in @($gameAssembly, $physicsModule, $coreModule, $globalManagers, $teamsterDll)) {
    if (-not (Test-Path $required -PathType Leaf)) {
        throw "Required audit input is missing: $required"
    }
}

function Get-TypeSource {
    param(
        [Parameter(Mandatory)][string]$Assembly,
        [Parameter(Mandatory)][string]$TypeName
    )

    $source = (& ilspycmd --disable-updatecheck -r $managed -t $TypeName $Assembly | Out-String)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($source)) {
        throw "Could not decompile $TypeName from $Assembly."
    }

    return [regex]::Replace($source, "\s+", " ").Trim()
}

$verified = [System.Collections.Generic.List[string]]::new()

function Assert-Contains {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Needle,
        [Parameter(Mandatory)][string]$Contract
    )

    if (-not $Source.Contains($Needle, [StringComparison]::Ordinal)) {
        throw "Navigation game contract moved: $Contract (missing: $Needle)"
    }

    $verified.Add($Contract)
}

function Assert-Matches {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Pattern,
        [Parameter(Mandatory)][string]$Contract
    )

    if (-not [regex]::IsMatch($Source, $Pattern)) {
        throw "Navigation game contract moved: $Contract (no match for: $Pattern)"
    }

    $verified.Add($Contract)
}

function Get-FileIdentity {
    param([Parameter(Mandatory)][string]$Path)

    $item = Get-Item $Path
    [ordered]@{
        file = $item.Name
        bytes = $item.Length
        sha256 = (Get-FileHash $Path -Algorithm SHA256).Hash
    }
}

# --- 0. Which game this is --------------------------------------------------
$version = Get-TypeSource -Assembly $gameAssembly -TypeName "Version"
$versionMatch = [regex]::Match($version, 'CurrentVersion \{ get; \} = new GameVersion\((\d+), (\d+), (\d+)\)')
$networkMatch = [regex]::Match($version, 'c_networkVersion = (\d+)u;')
if (-not $versionMatch.Success -or -not $networkMatch.Success) {
    throw "Could not resolve the Valheim version from assembly_valheim.dll."
}
$gameVersion = '{0}.{1}.{2}' -f $versionMatch.Groups[1].Value, $versionMatch.Groups[2].Value, $versionMatch.Groups[3].Value

$manifest = Join-Path (Split-Path (Split-Path $environment.ValheimInstall -Parent) -Parent) "appmanifest_892970.acf"
$steamBuildId = $null
if (Test-Path $manifest) {
    $buildLine = Select-String -Path $manifest -Pattern '"buildid"\s+"(\d+)"' | Select-Object -First 1
    if ($null -ne $buildLine) { $steamBuildId = $buildLine.Matches[0].Groups[1].Value }
}

# --- 1. The navmesh (NavmeshCartPathSource) ---------------------------------
$pathfinding = Get-TypeSource -Assembly $gameAssembly -TypeName "Pathfinding"
Assert-Contains $pathfinding 'public static Pathfinding instance => m_instance;' `
    "Pathfinding.instance is the navmesh singleton"
Assert-Contains $pathfinding 'public bool GetPath(Vector3 from, Vector3 to, List<Vector3> path, AgentType agentType, bool requireFullPath = false, bool cleanup = true, bool havePath = false)' `
    "Pathfinding.GetPath keeps its seven-parameter public signature"
Assert-Contains $pathfinding 'if (requireFullPath) { return false; }' `
    "Pathfinding.GetPath refuses a partial path when a full one is required"
Assert-Matches $pathfinding 'AgentSettings (\w+) = AddAgent\(AgentType\.HorseSize\); \1\.m_build\.agentHeight = 2\.5f; \1\.m_build\.agentClimb = 0\.3f; \1\.m_build\.agentRadius = 0\.8f; \1\.m_build\.agentSlope = 85f;' `
    "the HorseSize agent is 2.5 m tall, steps 0.3 m and has a 0.8 m radius"
Assert-Contains $pathfinding 'NavMesh.SamplePosition(point, out hit, 12f, filter)' `
    "GetPath may snap an end up to 12 m onto the navmesh (the planner joins the exact ends itself)"

# --- 2. Loaded ground, water, lava (GameCartTerrainProbe.SampleGround) -------
$zoneSystem = Get-TypeSource -Assembly $gameAssembly -TypeName "ZoneSystem"
Assert-Contains $zoneSystem 'public static ZoneSystem instance => s_instance;' "ZoneSystem.instance is the zone singleton"
Assert-Contains $zoneSystem 'public bool IsZoneLoaded(Vector3 point)' "ZoneSystem.IsZoneLoaded(Vector3) exists"
Assert-Contains $zoneSystem 'public float m_waterLevel = 30f;' "ZoneSystem.m_waterLevel is the sea level"
Assert-Contains $zoneSystem 'm_solidRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");' `
    "the game's own solid ground layers are the probe's ground layers"
Assert-Contains $zoneSystem 'm_blockRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");' `
    "the game's own blocking layers are the probe's obstacle layers (plus vehicle)"

$heightmap = Get-TypeSource -Assembly $gameAssembly -TypeName "Heightmap"
Assert-Contains $heightmap 'public static Heightmap FindHeightmap(Vector3 point)' "Heightmap.FindHeightmap(Vector3) finds a loaded terrain tile"
Assert-Contains $heightmap 'public bool IsLava(Vector3 worldPos, float lavaValue = 0.6f)' "Heightmap.IsLava(Vector3, float) exists"

$floating = Get-TypeSource -Assembly $gameAssembly -TypeName "Floating"
Assert-Contains $floating 'public static float GetLiquidLevel(Vector3 p, float waveFactor = 1f, LiquidType type = LiquidType.All)' `
    "Floating.GetLiquidLevel reads water and tar at a point"
Assert-Contains $floating 'float num = -10000f;' "GetLiquidLevel answers far below the ground where there is no liquid"

$liquidType = Get-TypeSource -Assembly $gameAssembly -TypeName "LiquidType"
Assert-Contains $liquidType 'public enum LiquidType { Water = 0, Tar = 1, All = 10 }' "LiquidType.All covers water and tar"

# --- 3. Doors (GameCartTerrainProbe.TryFindDoorways) -------------------------
$piece = Get-TypeSource -Assembly $gameAssembly -TypeName "Piece"
Assert-Contains $piece 'public static void GetAllPiecesInRadius(Vector3 p, float radius, List<Piece> pieces)' `
    "Piece.GetAllPiecesInRadius lists loaded pieces without a physics query"
$door = Get-TypeSource -Assembly $gameAssembly -TypeName "Door"
Assert-Contains $door 'public class Door : MonoBehaviour, Hoverable, Interactable' "Door is the door component"
Assert-Contains $door 'public string m_name = "door";' "Door.m_name (the capability presence check)"

# --- 4. The cart (CartFootprintReader) ---------------------------------------
$cart = Get-TypeSource -Assembly $gameAssembly -TypeName "Vagon"
Assert-Contains $cart 'public Transform m_attachPoint;' "Vagon.m_attachPoint is the handle"
Assert-Contains $cart 'public Rigidbody[] m_wheels = new Rigidbody[0];' "Vagon.m_wheels are the wheel bodies"

# --- 5. The walking corner radius the steering goals use ---------------------
$baseAi = Get-TypeSource -Assembly $gameAssembly -TypeName "BaseAI"
Assert-Contains $baseAi 'float num = (run ? 1f : 0.5f);' "vanilla path following accepts a corner within 0.5 m when walking"

# --- 6. Unity physics and layers ---------------------------------------------
$attribute = '(?:\[[^\]]*\] )?'
$physics = Get-TypeSource -Assembly $physicsModule -TypeName "UnityEngine.Physics"
Assert-Matches $physics ('public static int RaycastNonAlloc\(Vector3 origin, Vector3 direction, RaycastHit\[\] results, ' +
    $attribute + 'float maxDistance, ' + $attribute + 'int layerMask, ' + $attribute + 'QueryTriggerInteraction queryTriggerInteraction\)') `
    "Physics.RaycastNonAlloc(origin, direction, results, distance, mask, triggers)"
Assert-Matches $physics ('public static int BoxCastNonAlloc\(Vector3 center, Vector3 halfExtents, Vector3 direction, RaycastHit\[\] results, ' +
    $attribute + 'Quaternion orientation, ' + $attribute + 'float maxDistance, ' + $attribute + 'int layerMask, ' + $attribute +
    'QueryTriggerInteraction queryTriggerInteraction\)') `
    "Physics.BoxCastNonAlloc(center, halfExtents, direction, results, orientation, distance, mask, triggers)"
Assert-Matches $physics ('public static int OverlapBoxNonAlloc\(Vector3 center, Vector3 halfExtents, Collider\[\] results, ' +
    $attribute + 'Quaternion orientation, ' + $attribute + 'int mask, ' + $attribute + 'QueryTriggerInteraction queryTriggerInteraction\)') `
    "Physics.OverlapBoxNonAlloc(center, halfExtents, results, orientation, mask, triggers)"

$layerMask = Get-TypeSource -Assembly $coreModule -TypeName "UnityEngine.LayerMask"
Assert-Contains $layerMask 'public static int GetMask(params string[] layerNames)' "LayerMask.GetMask(params string[])"
Assert-Contains $layerMask 'public static string LayerToName(int layer)' "LayerMask.LayerToName(int)"
Assert-Matches $layerMask 'public (?:unsafe )?static int NameToLayer\(string layerName\)' `
    "LayerMask.NameToLayer(string) (GetMask resolves names through it)"

# The layer table lives in globalgamemanagers as length-prefixed strings.
$latin1 = [Text.Encoding]::GetEncoding(28591)
$managerText = $latin1.GetString([IO.File]::ReadAllBytes($globalManagers))
$layers = @("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle", "item", "character",
    "character_net", "character_noenv", "piece_nonsolid", "WaterVolume")
foreach ($layer in $layers) {
    $prefix = $latin1.GetString([BitConverter]::GetBytes([int]$layer.Length))
    if ($managerText.IndexOf($prefix + $layer, [StringComparison]::Ordinal) -lt 0) {
        throw "Navigation game contract moved: layer '$layer' is not in the installed TagManager."
    }
}
$verified.Add("TagManager has the layers the probe names or relies on: $($layers -join ', ')")

# --- 7. The built adapters use nothing else ----------------------------------
$il = (& ilspycmd --disable-updatecheck -il -r $output $teamsterDll | Out-String)
if ($LASTEXITCODE -ne 0) { throw "Could not disassemble the Teamster DLL." }
$classPattern = '(?ms)^\.class [^\r\n]*?TheConcernedCat\.ConcernedTeamster\.Adapters\.Navigation\.(\w+)\s.*?^\} // end of class TheConcernedCat\.ConcernedTeamster\.Adapters\.Navigation\.\1'
$classes = [regex]::Matches($il, $classPattern)
if ($classes.Count -lt 5) {
    throw "Expected the navigation adapter classes in the built DLL; found $($classes.Count)."
}

$auditedMembers = @(
    "Pathfinding::get_instance", "Pathfinding::GetPath",
    "ZoneSystem::get_instance", "ZoneSystem::IsZoneLoaded", "ZoneSystem::m_waterLevel",
    "Heightmap::FindHeightmap", "Heightmap::IsLava",
    "Floating::GetLiquidLevel",
    "Piece::GetAllPiecesInRadius",
    "Vagon::m_attachPoint", "Vagon::m_wheels"
)
$auditedTypes = @("Pathfinding", "Pathfinding/AgentType", "ZoneSystem", "Heightmap", "Floating", "LiquidType", "Piece", "Door", "Vagon")

$usedMembers = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
$usedTypes = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
foreach ($class in $classes) {
    foreach ($match in [regex]::Matches($class.Value, '\[assembly_valheim\]([A-Za-z_][\w\.+/`]*)::([\w\.<>`]+)')) {
        [void]$usedMembers.Add($match.Groups[1].Value + "::" + $match.Groups[2].Value)
    }

    foreach ($match in [regex]::Matches($class.Value, '\[assembly_valheim\]([A-Za-z_][\w\.+/`]*)')) {
        [void]$usedTypes.Add($match.Groups[1].Value)
    }
}

$unauditedMembers = @($usedMembers | Where-Object { $auditedMembers -notcontains $_ })
$unauditedTypes = @($usedTypes | Where-Object { $auditedTypes -notcontains $_ })
if ($unauditedMembers.Count -gt 0 -or $unauditedTypes.Count -gt 0) {
    throw ("The navigation adapters use game surface this audit does not cover: " +
        (@($unauditedMembers) + @($unauditedTypes) -join ", "))
}
$unusedMembers = @($auditedMembers | Where-Object { -not $usedMembers.Contains($_) })

$result = [ordered]@{
    result = "PASS"
    scope = "static audit of installed assemblies, the TagManager and the built Teamster IL; NOT live in-game evidence"
    valheimVersion = $gameVersion
    networkVersion = [int]$networkMatch.Groups[1].Value
    steamBuildId = $steamBuildId
    contractsVerified = $verified.Count
    contracts = $verified
    adapterClasses = @($classes | ForEach-Object { $_.Groups[1].Value })
    gameMembersUsed = @($usedMembers)
    gameTypesUsed = @($usedTypes)
    auditedButUnused = $unusedMembers
    gameAssembly = Get-FileIdentity $gameAssembly
    physicsModule = Get-FileIdentity $physicsModule
    globalGameManagers = Get-FileIdentity $globalManagers
    teamster = Get-FileIdentity $teamsterDll
}

$result | ConvertTo-Json -Depth 4
Write-Host "Teamster navigation game-API audit PASS."
