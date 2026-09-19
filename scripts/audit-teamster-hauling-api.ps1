[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipBuild
)

# #313 (CT-NPC-002): verifies, against the INSTALLED game, every member Gunnar's
# hauling runtime binds (src/ConcernedTeamster/Adapters/Workers/HaulingCapabilityProbe.cs
# is the runtime twin of this list; the two change together), the vanilla
# behaviour the design depends on (DECISIONS.md D4/D5), and the built Teamster
# DLL's IL: the cart's attach/detach only from the worker runtime, mass and
# network-object writes only where the scoped validator allows them, and no
# teleport, pose, velocity, force, ownership or RPC call anywhere.
# Read-only: it decompiles and compares, it never runs or changes the game.

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
$physicsAssembly = Join-Path $managed "UnityEngine.PhysicsModule.dll"
$globalManagers = Join-Path $environment.ValheimInstall "valheim_Data\globalgamemanagers"
$output = Join-Path $root "src\ConcernedTeamster\bin\$Configuration\net48"
$teamsterDll = Join-Path $output "TheConcernedCat.ConcernedTeamster.dll"
$publicizedAssembly = Join-Path $output "assembly_valheim_publicized.dll"
$bepInExCore = Join-Path $environment.BepInExPath "core"
$bepInExDll = Join-Path $bepInExCore "BepInEx.dll"
$jotunnDll = Join-Path $environment.BepInExPath "plugins\ValheimModding-Jotunn\Jotunn.dll"

foreach ($required in @($gameAssembly, $physicsAssembly, $globalManagers, $teamsterDll, $publicizedAssembly, $bepInExDll, $jotunnDll)) {
    if (-not (Test-Path $required -PathType Leaf)) {
        throw "Required audit input is missing: $required"
    }
}

function Get-FileIdentity {
    param([Parameter(Mandatory)][string]$Path)

    $item = Get-Item $Path
    [ordered]@{
        file = $item.Name
        bytes = $item.Length
        sha256 = (Get-FileHash $Path -Algorithm SHA256).Hash
        assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($Path).Version.ToString()
    }
}

$sourceCache = @{}
function Get-TypeSource {
    param(
        [Parameter(Mandatory)][string]$TypeName,
        [Parameter(Mandatory)][ValidateSet("game", "physics", "jotunn")][string]$Assembly
    )

    $key = "$Assembly|$TypeName"
    if (-not $sourceCache.ContainsKey($key)) {
        $text = switch ($Assembly) {
            "game" { & ilspycmd --disable-updatecheck -r $managed -t $TypeName $gameAssembly | Out-String }
            "physics" { & ilspycmd --disable-updatecheck -r $managed -t $TypeName $physicsAssembly | Out-String }
            "jotunn" { & ilspycmd --disable-updatecheck -r $managed -r $bepInExCore -t $TypeName $jotunnDll | Out-String }
        }

        if ($LASTEXITCODE -ne 0) { $text = "" }
        $sourceCache[$key] = [regex]::Replace($text, '\s+', ' ')
    }

    return $sourceCache[$key]
}

# Type | assembly | what | regex over whitespace-normalized decompiled source.
$requirements = @(
    # The attach seam (CONTRACTS.md §2.5) and the cart facts the D4 preconditions read.
    @("Vagon", "game", "Vagon.AttachTo(GameObject)", 'private void AttachTo\(GameObject \w+\)'),
    @("Vagon", "game", "Vagon.Detach()", 'private void Detach\(\)'),
    @("Vagon", "game", "Vagon.m_attachJoin", 'private ConfigurableJoint m_attachJoin;'),
    @("Vagon", "game", "Vagon.m_bodies", 'private Rigidbody\[\] m_bodies;'),
    @("Vagon", "game", "Vagon.m_attachPoint", 'public Transform m_attachPoint;'),
    @("Vagon", "game", "Vagon.m_attachOffset", 'public Vector3 m_attachOffset\b'),
    @("Vagon", "game", "Vagon.m_detachDistance", 'public float m_detachDistance\b'),
    @("Vagon", "game", "Vagon.m_breakForce", 'public float m_breakForce\b'),
    @("Vagon", "game", "Vagon.m_spring", 'public float m_spring\b'),
    @("Vagon", "game", "Vagon.m_springDamping", 'public float m_springDamping\b'),
    @("Vagon", "game", "Vagon.m_playerExtraPullMass", 'public float m_playerExtraPullMass;'),
    @("Vagon", "game", "Vagon.m_baseMass", 'public float m_baseMass\b'),
    @("Vagon", "game", "Vagon.m_itemWeightMassFactor", 'public float m_itemWeightMassFactor\b'),
    @("Vagon", "game", "Vagon.m_container", 'public Container m_container;'),
    @("Vagon", "game", "Vagon.m_chair (someone sitting in the cart, D4.4)", '\bChair m_chair\b'),
    @("Chair", "game", "Chair.IsInUse()", 'public bool IsInUse\(\)'),
    @("Vagon", "game", "Vagon.m_nview", 'private ZNetView m_nview;'),
    @("Vagon", "game", "Vagon.m_body", 'private Rigidbody m_body;'),
    @("Vagon", "game", "Vagon.m_instances", 'private static List<Vagon> m_instances\b'),
    @("Vagon", "game", "Vagon.InUse()", 'public bool InUse\(\)'),
    @("Vagon", "game", "Vagon.IsAttached()", 'public bool IsAttached\(\)'),
    @("ZDOVars", "game", "ZDOVars.s_attachJointHash", 'public static readonly int s_attachJointHash = "attachJoint"\.GetStableHashCode\(\);'),

    # Vanilla behaviour the design relies on (D4, D5).
    @("Vagon", "game", "AttachTo detaches every loaded cart first (D4.6)", 'private void AttachTo\(GameObject go\) \{ DetachAll\(\);'),
    @("Vagon", "game", "AttachTo connects the exact object's rigidbody (D4.9)", 'm_attachJoin\.connectedBody = go\.GetComponent<Rigidbody>\(\);'),
    @("Vagon", "game", "AttachTo sets the attach flag", 'GetZDO\(\)\.Set\(ZDOVars\.s_attachJointHash, value: true\);'),
    @("Vagon", "game", "AttachTo adds the player pull mass to a Character puller (D5)", 'm_attachedObject\.GetComponent<Character>\(\)\?\.SetExtraMass\(m_playerExtraPullMass\);'),
    @("Vagon", "game", "Detach clears the flag only as owner", 'if \(m_nview\.IsValid\(\) && m_nview\.IsOwner\(\)\) \{ m_nview\.GetZDO\(\)\.Set\(ZDOVars\.s_attachJointHash, value: false\); \}'),
    @("Vagon", "game", "CanAttach measures puller + attachOffset to the handle (D4.8)", 'Vector3\.Distance\(go\.transform\.position \+ m_attachOffset, m_attachPoint\.position\) < m_detachDistance'),
    @("Vagon", "game", "CanAttach lets go below up.y 0.1 (CartTipped)", 'if \(base\.transform\.up\.y < 0\.1f\)'),
    @("Vagon", "game", "A non-owner never keeps a joint (OwnershipLost)", 'else if \(IsAttached\(\)\) \{ Detach\(\); \}'),
    @("Character", "game", "SetExtraMass writes originalMass + amount (D5)", 'public void SetExtraMass\(float amount\) \{ m_body\.mass = m_originalMass \+ amount; \}'),
    @("Character", "game", "Awake captures the base mass (D5)", 'm_originalMass = m_body\.mass;'),

    # Network identity and ownership, read only, plus the worker's own key.
    @("ZNetView", "game", "ZNetView.IsValid()", 'public bool IsValid\(\)'),
    @("ZNetView", "game", "ZNetView.IsOwner()", 'public bool IsOwner\(\)'),
    @("ZNetView", "game", "ZNetView.GetZDO()", 'public ZDO GetZDO\(\)'),
    @("ZNetView", "game", "ZNetView.Destroy()", 'public void Destroy\(\)'),
    @("ZNetView", "game", "ZNetView.m_persistent", 'public bool m_persistent;'),
    @("ZDO", "game", "ZDO.GetBool(int, bool)", 'public bool GetBool\(int hash, bool defaultValue = false\)'),
    @("ZDO", "game", "ZDO.GetString(string, string)", 'public string GetString\(string name, string defaultValue = ""\)'),
    @("ZDO", "game", "ZDO.Set(string, string)", 'public void Set\(string name, string value\)'),
    @("ZDO", "game", "ZDO.GetOwner()", 'public long GetOwner\(\)'),
    @("ZDO", "game", "ZDO.m_uid", 'public ZDOID m_uid\b'),
    @("ZDOID", "game", "ZDOID(long, uint) and ==", 'public ZDOID\(long userID, uint id\).*public static bool operator ==\(ZDOID a, ZDOID b\)'),
    @("ZDOMan", "game", "ZDOMan.instance", 'public static ZDOMan instance =>'),
    @("ZDOMan", "game", "ZDOMan.GetZDO(ZDOID)", 'public ZDO GetZDO\(ZDOID id\)'),
    @("ZDOMan", "game", "ZDOMan.GetSessionID()", 'public static long GetSessionID\(\)'),
    @("ZDOMan", "game", "ZDOMan.GetAllZDOsWithPrefabIterative", 'public bool GetAllZDOsWithPrefabIterative\(string prefab, List<ZDO> zdos, ref int index\)'),

    # Gunnar's body and motor.
    @("Character", "game", "Character.m_originalMass", 'private float m_originalMass;'),
    @("Character", "game", "Character.IsDead()", 'public virtual bool IsDead\(\)'),
    @("Character", "game", "Character.SetWalk(bool)", 'public void SetWalk\(bool walk\)'),
    @("Character", "game", "Character.SetRun(bool)", 'public void SetRun\(bool run\)'),
    @("Character", "game", "Character.m_faction / m_boss / m_name", 'public string m_name = "";.*public Faction m_faction\b.*public bool m_boss;'),
    @("BaseAI", "game", "BaseAI.Awake()", 'protected virtual void Awake\(\)'),
    @("BaseAI", "game", "BaseAI.OnEnable()/OnDisable()", 'protected virtual void OnEnable\(\).*protected virtual void OnDisable\(\)'),
    @("BaseAI", "game", "BaseAI.UpdateAI is an ownership gate without creature behaviour", 'public virtual bool UpdateAI\(float dt\) \{ if \(!m_nview\.IsValid\(\)\) \{ return false; \} if \(!m_nview\.IsOwner\(\)\)'),
    @("BaseAI", "game", "BaseAI.MoveTo(float, Vector3, float, bool)", 'protected bool MoveTo\(float dt, Vector3 point, float dist, bool run\)'),
    @("BaseAI", "game", "BaseAI.MoveTowards(Vector3, bool)", 'public void MoveTowards\(Vector3 dir, bool run\)'),
    @("BaseAI", "game", "BaseAI.StopMoving()", 'public void StopMoving\(\)'),
    @("BaseAI", "game", "BaseAI.LookTowards(Vector3)", 'public void LookTowards\(Vector3 dir\)'),
    @("BaseAI", "game", "BaseAI.FoundPath()", 'protected bool FoundPath\(\)'),
    @("BaseAI", "game", "BaseAI silencing fields", 'public EffectList m_idleSound\b.*public float m_idleSoundChance\b.*public string m_spawnMessage\b.*public string m_deathMessage\b.*public string m_alertedMessage\b.*public bool m_canBeAlerted\b'),
    @("Humanoid", "game", "Humanoid default and random gear", 'public GameObject\[\] m_defaultItems;.*public GameObject\[\] m_randomWeapon;.*public GameObject\[\] m_randomArmor;.*public GameObject\[\] m_randomShield;.*public ItemSet\[\] m_randomSets;.*public RandomItem\[\] m_randomItems;'),
    @("Player", "game", "Player.m_localPlayer", 'public static Player m_localPlayer\b'),
    @("Player", "game", "Player.GetHoverObject()", 'public override GameObject GetHoverObject\(\)'),

    # Components stripped from the worker clone, and the siege engines refused as carts.
    @("Tameable", "game", "Tameable", 'public class Tameable : MonoBehaviour'),
    @("Sadle", "game", "Sadle", 'public class Sadle : MonoBehaviour'),
    @("NpcTalk", "game", "NpcTalk", 'public class NpcTalk : MonoBehaviour'),
    @("CharacterDrop", "game", "CharacterDrop", 'public class CharacterDrop : MonoBehaviour'),
    @("Procreation", "game", "Procreation", 'public class Procreation : MonoBehaviour'),
    @("Growup", "game", "Growup", 'public class Growup : MonoBehaviour'),
    @("CharacterTimedDestruction", "game", "CharacterTimedDestruction", 'public class CharacterTimedDestruction : MonoBehaviour'),
    @("Catapult", "game", "Catapult", 'public class Catapult : MonoBehaviour'),
    @("SiegeMachine", "game", "SiegeMachine", 'public class SiegeMachine : MonoBehaviour'),

    # Cart contents (evidence only) and use.
    @("Container", "game", "Container.IsInUse()", 'public bool IsInUse\(\)'),
    @("Container", "game", "Container.GetInventory()", 'public Inventory GetInventory\(\)'),
    @("Inventory", "game", "Inventory.GetTotalWeight()", 'public float GetTotalWeight\(\)'),
    @("Inventory", "game", "Inventory.NrOfItems()", 'public int NrOfItems\(\)'),

    # Work authority and world lifecycle.
    @("ZNet", "game", "ZNet.instance", 'public static ZNet instance =>'),
    @("ZNet", "game", "ZNet.IsServer()", 'public bool IsServer\(\)'),
    @("ZNet", "game", "ZNet.IsDedicated()", 'public bool IsDedicated\(\)'),
    @("ZNet", "game", "ZNet.GetPeers()", 'public List<ZNetPeer> GetPeers\(\)'),
    @("ZNetScene", "game", "ZNetScene.instance", 'public static ZNetScene instance =>'),
    @("Game", "game", "Game.instance", 'public static Game instance \{ get; private set; \}'),
    @("Game", "game", "Game.IsShuttingDown()", 'public bool IsShuttingDown\(\)'),

    # Ground, water and loaded area.
    @("ZoneSystem", "game", "ZoneSystem.instance", 'public static ZoneSystem instance =>'),
    @("ZoneSystem", "game", "ZoneSystem.IsZoneLoaded(Vector3)", 'public bool IsZoneLoaded\(Vector3 point\)'),
    @("ZoneSystem", "game", "ZoneSystem.GetGroundHeight(Vector3, out float)", 'public bool GetGroundHeight\(Vector3 p, out float height\)'),
    @("Floating", "game", "Floating.GetLiquidLevel", 'public static float GetLiquidLevel\(Vector3 p, float waveFactor = 1f, LiquidType type = LiquidType\.All\)'),
    @("Heightmap", "game", "Heightmap.GetHeight(Vector3, out float)", 'public static bool GetHeight\(Vector3 worldPos, out float height\)'),

    # Engine members read by the joint and body checks.
    @("UnityEngine.Joint", "physics", "Joint.connectedBody", 'public Rigidbody connectedBody\b'),
    @("UnityEngine.Joint", "physics", "Joint.currentForce", 'public Vector3 currentForce\b'),
    @("UnityEngine.Rigidbody", "physics", "Rigidbody.isKinematic/useGravity/detectCollisions", 'public bool isKinematic\b.*public bool useGravity\b|public bool useGravity\b.*public bool isKinematic\b'),
    @("UnityEngine.Rigidbody", "physics", "Rigidbody.detectCollisions", 'public bool detectCollisions\b'),
    @("UnityEngine.Rigidbody", "physics", "Rigidbody.mass", 'public float mass\b'),
    @("UnityEngine.Rigidbody", "physics", "Rigidbody.constraints", 'public RigidbodyConstraints constraints\b'),
    @("UnityEngine.Rigidbody", "physics", "Rigidbody.linearVelocity", 'public Vector3 linearVelocity\b'),

    # The collection carve-out (#381, owner decision 2026-09-19). Every member
    # GunnarCollectionPort binds. The runtime twin is HaulingCapabilityProbe;
    # the two change together.
    @("Pickable", "game", "Pickable.Interact(Humanoid, bool, bool)", 'public bool Interact\(Humanoid character, bool repeat, bool alt\)'),
    @("Pickable", "game", "Pickable.Interact routes RPC_Pick rather than returning the yield", 'm_nview\.InvokeRPC\("RPC_Pick", num\);'),
    @("Pickable", "game", "RPC_Pick drops on the ground and needs the local player", 'private void RPC_Pick\(long sender, int bonus\).*Player\.m_localPlayer\.GetZDOID\(\)'),
    @("Pickable", "game", "RPC_Pick is owner-only", 'private void RPC_Pick\(long sender, int bonus\) \{ if \(!m_nview\.IsOwner\(\) \|\| m_picked\)'),
    @("Pickable", "game", "the skill, statistic and bonus branches are Player-only", 'if \(!m_picked && character is Player player\)'),
    @("Pickable", "game", "Pickable.CanBePicked()", 'public bool CanBePicked\(\)'),
    @("Pickable", "game", "Pickable.m_itemPrefab / m_amount / m_tarPreventsPicking", 'public GameObject m_itemPrefab;.*public int m_amount = 1;'),
    @("Humanoid", "game", "Humanoid.Pickup(GameObject, bool, bool)", 'public bool Pickup\(GameObject go, bool autoequip = true, bool autoPickupDelay = true\)'),
    @("Humanoid", "game", "Pickup takes through the inventory, not by fiat", 'public bool Pickup\(GameObject go, bool autoequip = true, bool autoPickupDelay = true\).*m_inventory\.ContainsItem\(component\.m_itemData\)'),
    @("ItemDrop", "game", "ItemDrop.m_itemData", 'public ItemData m_itemData = new ItemData\(\);'),

    # Jotunn: registering the worker prefab for every session.
    @("Jotunn.Managers.PrefabManager", "jotunn", "PrefabManager.OnVanillaPrefabsAvailable", 'public static event Action OnVanillaPrefabsAvailable;'),
    @("Jotunn.Managers.PrefabManager", "jotunn", "PrefabManager.CreateClonedPrefab(string, GameObject)", 'public GameObject CreateClonedPrefab\(string name, GameObject prefab\)'),
    @("Jotunn.Managers.PrefabManager", "jotunn", "PrefabManager.AddPrefab(GameObject)", 'public void AddPrefab\(GameObject prefab\)'),
    @("Jotunn.Managers.PrefabManager", "jotunn", "PrefabManager.GetPrefab(string)", 'public GameObject GetPrefab\(string name\)'),
    @("Jotunn.Managers.PrefabManager", "jotunn", "PrefabManager.DestroyPrefab(string)", 'public void DestroyPrefab\(string name\)'),
    @("Jotunn.Managers.PrefabManager", "jotunn", "Custom prefabs are registered on every ZNetScene.Awake", '\[HarmonyPatch\(typeof\(ZNetScene\), "Awake"\)\] \[HarmonyPostfix\] private static void RegisterAllToZNetScene\(\)')
)

$failures = New-Object System.Collections.Generic.List[string]
$verified = 0
foreach ($requirement in $requirements) {
    $source = Get-TypeSource -TypeName $requirement[0] -Assembly $requirement[1]
    if ([regex]::IsMatch($source, $requirement[3])) {
        $verified++
    } else {
        $failures.Add("game/engine member or behaviour not found as audited: $($requirement[2])")
    }
}

# IL audit of the built Teamster DLL. Each call is attributed to the top-level
# type whose IL contains it (nested compiler-generated types belong to it).
$teamsterIl = (& ilspycmd --disable-updatecheck -il $teamsterDll | Out-String)
if ($LASTEXITCODE -ne 0) { throw "Could not inspect the Teamster IL." }
$ilLines = $teamsterIl -split "`r?`n"
$currentType = ""
$calls = New-Object System.Collections.Generic.List[object]
for ($index = 0; $index -lt $ilLines.Length; $index++) {
    $line = $ilLines[$index]
    $classMatch = [regex]::Match($line, "^\.class\s+(?!.*\bnested\b).*?\s([\w.``<>'-]+)\s*$")
    if ($classMatch.Success) {
        $currentType = $classMatch.Groups[1].Value
        continue
    }

    if ($line -match '(call|callvirt|stfld|ldftn)\s') {
        $calls.Add([pscustomobject]@{ Type = $currentType; Line = $line.Trim(); Index = $index })
    }
}

$workersNamespace = "TheConcernedCat.ConcernedTeamster.Adapters.Workers."
$forbidden = @(
    'Rigidbody::set_velocity', 'Rigidbody::set_linearVelocity', 'Rigidbody::set_angularVelocity',
    'Rigidbody::set_position', 'Rigidbody::set_rotation', 'Rigidbody::MovePosition', 'Rigidbody::MoveRotation',
    'Rigidbody::AddForce', 'Rigidbody::AddTorque', 'Rigidbody::AddExplosionForce', 'Rigidbody::AddRelativeForce',
    'Rigidbody::set_isKinematic', 'Rigidbody::set_useGravity', 'Rigidbody::set_detectCollisions',
    'Transform::set_position', 'Transform::set_rotation', 'Transform::SetPositionAndRotation', 'Transform::set_localPosition',
    'Vagon::Interact', 'Vagon::RPC_RequestOwn', 'ZDO::SetOwner', 'ZDO::SetPosition', 'ZDO::SetRotation',
    'ZNetView::ClaimOwnership', 'ZNetView::InvokeRPC', 'ZRoutedRpc::', 'Character::SetExtraMass', '::TeleportTo',
    'Joint::set_connectedBody'
)

$ilSummary = [ordered]@{}
foreach ($token in $forbidden) {
    $hits = @($calls | Where-Object { $_.Line.Contains($token) })
    if ($hits.Count -gt 0) {
        $failures.Add("Teamster IL calls forbidden $token in $(@($hits | ForEach-Object Type | Select-Object -Unique) -join ', ')")
    }
}

# Exact counts, not a floor (review R-313 m7): a second attach, mass write or
# network-object write inside an allowed type is a change that has to be made on
# purpose, and shows up here the moment it is not.
function Assert-OnlyIn {
    param([string]$Token, [scriptblock]$Allowed, [string]$Rule, [int]$Expected = -1)

    $hits = @($calls | Where-Object { $_.Line.Contains($Token) })
    $ilSummary[$Token] = $hits.Count
    foreach ($hit in $hits) {
        if (-not (& $Allowed $hit)) {
            $failures.Add("Teamster IL uses $Token in $($hit.Type): $Rule")
        }
    }

    if ($Expected -ge 0 -and $hits.Count -ne $Expected) {
        $failures.Add("Teamster IL uses $Token $($hits.Count) time(s), expected exactly $Expected")
    }
}

Assert-OnlyIn 'Vagon::AttachTo' { param($h) $h.Type.StartsWith($workersNamespace) } "the cart's attach is called only by Gunnar's worker runtime" 1
Assert-OnlyIn 'Vagon::Detach(' { param($h) $h.Type.StartsWith($workersNamespace) } "the cart's detach is called only by Gunnar's worker runtime" 2
Assert-OnlyIn 'Vagon::DetachAll' { param($h) $false } "no Teamster code detaches every cart on the client" 0
Assert-OnlyIn 'Rigidbody::set_mass' { param($h) $h.Type -eq ($workersNamespace + "TeamsterWorkerBody") } "a mass is written only by Gunnar's own calibration" 1
Assert-OnlyIn 'stfld float32 [assembly_valheim]Character::m_originalMass' { param($h) $h.Type -eq ($workersNamespace + "TeamsterWorkerBody") } "the base mass is written only by Gunnar's own calibration" 1
Assert-OnlyIn 'Rigidbody::set_constraints' { param($h) $h.Type -eq "TheConcernedCat.ConcernedTeamster.Adapters.CartBrakeAdapter" } "constraints are written only by the parking brake" 2
Assert-OnlyIn 'ZDO::Set(' {
    param($h)
    if ($h.Type -ne ($workersNamespace + "TeamsterWorkerPrefab")) { return $false }
    $window = ($ilLines[[Math]::Max(0, $h.Index - 12)..$h.Index] -join "`n")
    return $window -match 'ldstr "tcc\.worker\.[a-z0-9.\-]+"'
} "a network object is written only with Gunnar's own tcc.worker.* key, by his prefab spawn" 1

$versionSource = Get-TypeSource -TypeName "Version" -Assembly "game"
$versionMatch = [regex]::Match($versionSource, 'CurrentVersion \{ get; \} = new GameVersion\((\d+), (\d+), (\d+)\)')
$gameVersion = if ($versionMatch.Success) { '{0}.{1}.{2}' -f $versionMatch.Groups[1].Value, $versionMatch.Groups[2].Value, $versionMatch.Groups[3].Value } else { $null }
if ($null -eq $gameVersion) { $failures.Add("could not resolve the Valheim GameVersion") }

$managerText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($globalManagers))
$unityMatch = [regex]::Match($managerText, '6000\.0\.\d+f\d+')

$manifest = Join-Path (Split-Path $environment.ValheimInstall -Parent) "..\appmanifest_892970.acf"
$steamBuildId = $null
if (Test-Path $manifest) {
    $buildLine = Select-String -Path $manifest -Pattern '"buildid"\s+"(\d+)"' | Select-Object -First 1
    if ($null -ne $buildLine) { $steamBuildId = $buildLine.Matches[0].Groups[1].Value }
}

$result = [ordered]@{
    result = if ($failures.Count -eq 0) { "PASS" } else { "FAIL" }
    valheimVersion = $gameVersion
    unityVersion = if ($unityMatch.Success) { $unityMatch.Value } else { $null }
    steamBuildId = $steamBuildId
    membersAndBehavioursVerified = $verified
    membersAndBehavioursAudited = $requirements.Count
    teamsterIlCallCounts = $ilSummary
    failures = @($failures)
    gameAssembly = Get-FileIdentity $gameAssembly
    physicsModule = Get-FileIdentity $physicsAssembly
    publicizedReference = Get-FileIdentity $publicizedAssembly
    bepinex = Get-FileIdentity $bepInExDll
    jotunn = Get-FileIdentity $jotunnDll
    teamster = Get-FileIdentity $teamsterDll
}

$result | ConvertTo-Json -Depth 5
if ($failures.Count -gt 0) {
    throw "Teamster hauling game-API audit FAILED: $($failures.Count) finding(s)."
}

Write-Host "Teamster hauling game-API audit PASS."
