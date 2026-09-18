[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    # Skip the product build (for a run right after a build through the build lock).
    [switch]$SkipBuild
)

# Issue #315 (CF-NPC-004) game-API audit: proves, against the INSTALLED Valheim
# assemblies, that every game member the Foreman collection adapters use still
# exists with the shape and the behaviour the adapters rely on
# (docs/mods/concerned-foreman/COLLECTION.md, PICKUP_SEAM_AUDIT.md), and that
# the built Foreman DLL never calls the forbidden paths.
#
# Static audit only. It reads the installed game; it does not launch it, and it
# proves nothing about in-game behaviour.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "RepoTools.psm1") -Force

$root = Get-RepoRoot
$environment = Get-EnvironmentValues -Root $root
Assert-Command ilspycmd
Assert-PathValue -Name "VALHEIM_INSTALL" -Path $environment.ValheimInstall
Assert-PathValue -Name "BEPINEX_PATH" -Path $environment.BepInExPath

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -Product ConcernedForeman
}

$managed = Join-Path $environment.ValheimInstall "valheim_Data\Managed"
$gameAssembly = Join-Path $managed "assembly_valheim.dll"
$utilsAssembly = Join-Path $managed "assembly_utils.dll"
$globalManagers = Join-Path $environment.ValheimInstall "valheim_Data\globalgamemanagers"
$foremanDll = Join-Path $root "src\ConcernedForeman\bin\$Configuration\net48\TheConcernedCat.ConcernedForeman.dll"
foreach ($required in @($gameAssembly, $utilsAssembly, $globalManagers, $foremanDll)) {
    if (-not (Test-Path $required -PathType Leaf)) {
        throw "Required audit input is missing: $required"
    }
}

function Get-TypeSource {
    param(
        [Parameter(Mandatory)][string]$TypeName,
        [string]$Assembly = $gameAssembly
    )

    $source = (& ilspycmd -t $TypeName $Assembly | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect type $TypeName."
    }

    return [regex]::Replace($source, "\s+", " ").Trim()
}

$checked = 0
function Assert-Contains {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Needle,
        [Parameter(Mandatory)][string]$Contract
    )

    if (-not $Source.Contains($Needle, [StringComparison]::Ordinal)) {
        throw "Foreman collection contract moved: $Contract (missing: $Needle)"
    }

    $script:checked++
}

function Assert-Order {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string[]]$Needles,
        [Parameter(Mandatory)][string]$Contract
    )

    $cursor = 0
    foreach ($needle in $Needles) {
        $index = $Source.IndexOf($needle, $cursor, [StringComparison]::Ordinal)
        if ($index -lt 0) {
            throw "Foreman collection contract moved: $Contract (missing or out of order: $needle)"
        }

        $cursor = $index + $needle.Length
    }

    $script:checked++
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

$versionSource = Get-TypeSource -TypeName "Version"
$versionMatch = [regex]::Match($versionSource, 'CurrentVersion\s*\{\s*get;\s*\}\s*=\s*new GameVersion\((\d+),\s*(\d+),\s*(\d+)\)')
if (-not $versionMatch.Success) { throw "Could not resolve Valheim GameVersion from assembly_valheim.dll." }
$gameVersion = '{0}.{1}.{2}' -f $versionMatch.Groups[1].Value, $versionMatch.Groups[2].Value, $versionMatch.Groups[3].Value

$managerText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($globalManagers))
$unityMatch = [regex]::Match($managerText, '6000\.0\.\d+f\d+')
if (-not $unityMatch.Success) { throw "Could not resolve the Unity 6000 version." }

# --- 1. Pickable: the predicate's facts and the pick itself ----------------------------------------------
$pickable = Get-TypeSource "Pickable"
foreach ($field in @(
        'public GameObject m_hideWhenPicked;',
        'public GameObject m_itemPrefab;',
        'public int m_amount = 1;',
        'public int m_minAmountScaled = 1;',
        'public bool m_dontScale;',
        'public float m_hoverOffset;',
        'public DropTable m_extraDrops = new DropTable();',
        'public float m_respawnTimeMinutes;',
        'public float m_aggravateRange;',
        'public int GetEnabled => m_enabled;',
        'public bool GetPicked() { return m_picked; }',
        'public bool CanBePicked()')) {
    Assert-Contains $pickable $field "Pickable member the classifier reads"
}

Assert-Order $pickable @(
    'public bool Interact(Humanoid character, bool repeat, bool alt)',
    'if (!m_picked && character is Player player)',
    'm_nview.InvokeRPC("RPC_Pick", num);') "Pickable.Interact accepts any Humanoid and grants player-only bonuses to players only"
Assert-Order $pickable @(
    'private void RPC_Pick(long sender, int bonus) { if (!m_nview.IsOwner() || m_picked) { return; }',
    'Player.m_localPlayer.GetZDOID()',
    'int num = (m_dontScale ? m_amount : Mathf.Max(m_minAmountScaled, Game.instance.ScaleDrops(m_itemPrefab, m_amount)));',
    'Drop(m_itemPrefab, num2++, 1);',
    'm_nview.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", true);') "RPC_Pick: owner-only, local player dereferenced, the yield expression ExpectedYield mirrors, one stack-1 drop per unit"
Assert-Order $pickable @(
    'GameObject obj = UnityEngine.Object.Instantiate(prefab, position, rotation);',
    'ItemDrop component = obj.GetComponent<ItemDrop>();',
    'ItemDrop.OnCreateNew(component);') "Pickable.Drop instantiates world item drops, never inventory items"

Assert-Contains (Get-TypeSource "DropTable") 'public bool IsEmpty()' "DropTable.IsEmpty (C3 extra drops)"

# --- 2. ItemDrop: tracing and guarding the spawned drops -------------------------------------------------
$itemDrop = Get-TypeSource "ItemDrop"
foreach ($member in @(
        'private static List<ItemDrop> s_instances = new List<ItemDrop>();',
        'public bool m_autoPickup = true;',
        'public ItemData m_itemData = new ItemData();',
        'public int m_stack = 1;',
        'public int m_quality = 1;',
        'public int m_variant;',
        'public SharedData m_shared;',
        'public float m_weight = 1f;')) {
    Assert-Contains $itemDrop $member "ItemDrop member the pickup port reads"
}

Assert-Contains $itemDrop 's_instances.Add(this);' "every new drop registers itself in ItemDrop.Awake"
Assert-Order $itemDrop @(
    'public bool CanPickup(bool autoPickupDelay = true)',
    'if (autoPickupDelay && (double)(Time.time - m_spawnTime) < 0.5)') "the player's auto-pickup waits 0.5 s after spawn"

# --- 3. Humanoid and Inventory: the take ------------------------------------------------------------------
$humanoid = Get-TypeSource "Humanoid"
Assert-Order $humanoid @(
    'public bool Pickup(GameObject go, bool autoequip = true, bool autoPickupDelay = true)',
    'if (!component.CanPickup(autoPickupDelay))',
    'bool flag = m_inventory.AddItem(component.m_itemData);',
    'if (m_nview.GetZDO() == null) { UnityEngine.Object.Destroy(go); return true; }',
    'ZNetScene.instance.Destroy(go);') "Humanoid.Pickup adds before it destroys the drop's network object"
Assert-Contains $humanoid 'public Inventory GetInventory()' "Humanoid.GetInventory"
Assert-Contains (Get-TypeSource "Inventory") 'public int CountItems(string name, int quality = -1, bool matchWorldLevel = true)' "Inventory.CountItems for the count delta"

$scene = Get-TypeSource "ZNetScene"
Assert-Contains $scene 'private readonly Dictionary<ZDO, ZNetView> m_instances = new Dictionary<ZDO, ZNetView>();' "the survey reads the scene's own instance table"
Assert-Contains $scene 'public GameObject GetPrefab(string name)' "ZNetScene.GetPrefab (yield per pick at acceptance)"
Assert-Order $scene @(
    'public void Destroy(GameObject go)',
    'component.ResetZDO();',
    'if (zDO.IsOwner()) { ZDOMan.instance.DestroyZDO(zDO); }') "a destroyed owned drop's ZDO is destroyed, and its view stops being valid"

# --- 4. Identity, provenance, ownership -------------------------------------------------------------------
$view = Get-TypeSource "ZNetView"
foreach ($member in @('public bool IsOwner()', 'public ZDO GetZDO()', 'public bool IsValid()')) {
    Assert-Contains $view $member "ZNetView member"
}

$zdo = Get-TypeSource "ZDO"
foreach ($member in @(
        'public int GetPrefab()',
        'public long GetLong(int hash, long defaultValue = 0L)',
        'public string GetString(string name, string defaultValue = "")',
        'public ZDOID m_uid = ZDOID.None;')) {
    Assert-Contains $zdo $member "ZDO member"
}

$zdoid = Get-TypeSource "ZDOID"
Assert-Contains $zdoid 'public long UserID => GetUserID(UserKey);' "ZDOID.UserID"
Assert-Contains $zdoid 'public uint ID { get; private set; }' "ZDOID.ID"
Assert-Contains (Get-TypeSource "ZDOVars") 'public static readonly int s_creator = "creator".GetStableHashCode();' "ZDOVars.s_creator (C4)"

$utils = Get-TypeSource -TypeName "Utils" -Assembly $utilsAssembly
Assert-Contains $utils 'public static string GetPrefabName(GameObject gameObject)' "Utils.GetPrefabName"
Assert-Contains $utils 'public static float DistanceXZ(Vector3 v0, Vector3 v1)' "Utils.DistanceXZ"
Assert-Contains (Get-TypeSource -TypeName "StringExtensionMethods" -Assembly $utilsAssembly) 'public static int GetStableHashCode(this string str)' "the prefab hash function"

# --- 5. Site clauses --------------------------------------------------------------------------------------
Assert-Contains (Get-TypeSource "Character") 'public static bool InInterior(Vector3 position) { return position.y > 3000f; }' "Character.InInterior"
$location = Get-TypeSource "Location"
Assert-Contains $location 'public static bool IsInsideLocation(Vector3 point, float distance)' "Location.IsInsideLocation"
# M3: the loaded-component list is filled in Awake, which is why it cannot be
# the only answer — the world's own registry is read as well.
Assert-Contains $location 'private static List<Location> s_allLocations = new List<Location>();' `
    "IsInsideLocation reads only the locations whose objects exist"
Assert-Order $location @(
    'private void Awake()',
    's_allLocations.Add(this);') "a location joins that list only when its component awakes"
$zoneLocationRegistry = Get-TypeSource "ZoneSystem"
foreach ($member in @(
        'public Dictionary<Vector2s, LocationInstance> m_locationInstances = new Dictionary<Vector2s, LocationInstance>();',
        'public static Vector2s GetZone(Vector3 point)',
        'public ZoneLocation m_location;',
        'public Vector3 m_position;',
        'public float m_exteriorRadius;')) {
    Assert-Contains $zoneLocationRegistry $member "the world's location registry, which answers before a location is built"
}
Assert-Contains (Get-TypeSource "PrivateArea") 'public static bool CheckAccess(Vector3 point, float radius = 0f, bool flash = true, bool wardCheck = false)' "PrivateArea.CheckAccess, through WorldDesignationSite"
Assert-Contains (Get-TypeSource "Player") 'public static Player m_localPlayer = null;' "Player.m_localPlayer (the pick path dereferences it)"
$zones = Get-TypeSource "ZoneSystem"
Assert-Contains $zones 'public bool IsZoneLoaded(Vector3 point)' "ZoneSystem.IsZoneLoaded"
Assert-Contains $zones 'public bool GetLocationIcon(string name, out Vector3 pos)' "ZoneSystem.GetLocationIcon (world start)"

# --- 6. Authority, the respawn anchor, the body, item weights ---------------------------------------------
$net = Get-TypeSource "ZNet"
foreach ($member in @('public bool IsServer()', 'public bool IsDedicated()', 'public List<ZNetPeer> GetPeers()', 'public long GetWorldUID()')) {
    Assert-Contains $net $member "ZNet member (D3 authority)"
}

$game = Get-TypeSource "Game"
Assert-Contains $game 'public string m_StartLocation = "StartTemple";' "the game's own start location name"
Assert-Contains $game 'public static float m_resourceRate = 1f;' "the world resource rate"
Assert-Contains $game 'public int ScaleDrops(GameObject drop, int amount)' "Game.ScaleDrops"
Assert-Contains $game 'public PlayerProfile GetPlayerProfile()' "Game.GetPlayerProfile"
Assert-Order $game @(
    'private bool FindSpawnPoint(out Vector3 point, out bool usedLogoutPoint, float dt)',
    'if (m_playerProfile.HaveCustomSpawnPoint())',
    'Bed bed = FindBedNearby(customSpawnPoint, 5f);',
    'if (ZoneSystem.instance.GetLocationIcon(m_StartLocation, out var pos))') "vanilla respawn: a current bed, else the start location"
Assert-Contains $game 'if (bed.IsCurrent()) { return bed; }' "vanilla honours only a current bed"

$profile = Get-TypeSource "PlayerProfile"
foreach ($member in @('public Vector3 GetCustomSpawnPoint()', 'public bool HaveCustomSpawnPoint()', 'public string GetName()')) {
    Assert-Contains $profile $member "PlayerProfile member"
}

Assert-Contains (Get-TypeSource "Bed") 'public bool IsCurrent()' "Bed.IsCurrent"
$baseAi = Get-TypeSource "BaseAI"
Assert-Contains $baseAi 'public static List<BaseAI> BaseAIInstances { get; } = new List<BaseAI>();' "BaseAI.BaseAIInstances (finding the body)"
Assert-Contains $baseAi 'protected ZNetView m_nview;' "BaseAI.m_nview"
Assert-Contains (Get-TypeSource "ObjectDB") 'public GameObject GetItemPrefab(string name)' "ObjectDB.GetItemPrefab (unit weights)"

# --- 7. The C2 component types exist ------------------------------------------------------------------------
$classes = (& ilspycmd -l c $gameAssembly | Out-String)
if ($LASTEXITCODE -ne 0) { throw "Could not list the game's classes." }
foreach ($type in @('Piece', 'WearNTear', 'Plant', 'Destructible', 'ItemDrop', 'Container', 'ItemStand', 'Procreation', 'Character', 'PickableItem', 'Pickable', 'Bed', 'Humanoid')) {
    if (-not [regex]::IsMatch($classes, "(?m)^Class $type\r?$")) {
        throw "Foreman collection contract moved: class $type no longer exists."
    }

    $checked++
}

# --- 8. The built DLL never takes the forbidden paths ------------------------------------------------------
$foremanIl = (& ilspycmd -il $foremanDll | Out-String)
if ($LASTEXITCODE -ne 0) { throw "Could not inspect the Foreman IL." }
$forbidden = [ordered]@{
    'ZNetView::ClaimOwnership' = "claiming ownership (never claimed)"
    'ZDO::SetOwner' = "setting an owner"
    'ItemDrop::Pickup' = "ItemDrop.Pickup (the non-player trap)"
    'ItemDrop::Interact' = "ItemDrop.Interact (the non-player trap)"
    'Vagon::AttachTo' = "attaching a cart, which belongs to Teamster alone"
    'Vagon::Detach' = "detaching a cart, which belongs to Teamster alone"
}
foreach ($token in $forbidden.Keys) {
    if ($foremanIl.Contains($token, [StringComparison]::Ordinal)) {
        throw "The Foreman DLL calls a forbidden path: $($forbidden[$token]) ($token)."
    }
}

$required = @(
    'Pickable::Interact', 'Humanoid::Pickup', 'ItemDrop::s_instances', 'ZNetScene::m_instances',
    'ZoneSystem::m_locationInstances', 'Location::IsInsideLocation')
foreach ($token in $required) {
    if (-not $foremanIl.Contains($token, [StringComparison]::Ordinal)) {
        throw "The Foreman DLL does not use $token; the audit no longer describes the adapters."
    }
}

$manifest = Join-Path (Split-Path $environment.ValheimInstall -Parent) "..\appmanifest_892970.acf"
$steamBuildId = $null
if (Test-Path $manifest) {
    $buildLine = Select-String -Path $manifest -Pattern '"buildid"\s+"(\d+)"' | Select-Object -First 1
    if ($null -ne $buildLine) { $steamBuildId = $buildLine.Matches[0].Groups[1].Value }
}

$result = [ordered]@{
    result = "PASS"
    valheimVersion = $gameVersion
    unityVersion = $unityMatch.Value
    steamBuildId = $steamBuildId
    contractsChecked = $checked
    forbiddenPathsAbsent = @($forbidden.Keys)
    gameAssembly = Get-FileIdentity $gameAssembly
    utilsAssembly = Get-FileIdentity $utilsAssembly
    foreman = Get-FileIdentity $foremanDll
}

$result | ConvertTo-Json -Depth 4
Write-Host "Foreman collection game-API audit PASS."
