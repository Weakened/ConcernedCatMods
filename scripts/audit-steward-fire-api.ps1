[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    # Skip the product build (for a run right after a build through the build lock).
    [switch]$SkipBuild
)

# Issue #340 (CS-001) game-API audit: proves, against the INSTALLED Valheim
# assemblies, that every game member the Concerned Steward fire adapter relies
# on still exists with the shape and the behaviour it relies on
# (docs/mods/concerned-steward/VALHEIM_FIRE_API_AUDIT.md), and that the BUILT
# Steward DLL never calls the forbidden paths.
#
# Why the built DLL and not only the source. The unit suite scans the sources,
# which is the check a reviewer can read. This one disassembles what actually
# ships, which is the check that survives a source file being added to the
# project without anybody noticing.
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
    & (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -Product ConcernedSteward
}

$managed = Join-Path $environment.ValheimInstall "valheim_Data\Managed"
$gameAssembly = Join-Path $managed "assembly_valheim.dll"
$stewardDll = Join-Path $root "src\ConcernedSteward\bin\$Configuration\net48\TheConcernedCat.ConcernedSteward.dll"
foreach ($required in @($gameAssembly, $stewardDll)) {
    if (-not (Test-Path $required -PathType Leaf)) {
        throw "Required audit input is missing: $required"
    }
}

$hash = (Get-FileHash -Algorithm SHA256 -Path $gameAssembly).Hash.ToLowerInvariant()
Write-Host "Auditing against assembly_valheim.dll SHA-256 $hash"

function Get-TypeSource {
    param(
        [Parameter(Mandatory)][string]$TypeName,
        [string]$Assembly = $gameAssembly
    )

    $source = (& ilspycmd --disable-updatecheck -r $managed -t $TypeName $Assembly | Out-String)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($source)) {
        throw "Could not decompile $TypeName from $Assembly."
    }

    return [regex]::Replace($source, "\s+", " ").Trim()
}

$checked = 0
$failures = [System.Collections.Generic.List[string]]::new()

function Assert-Contains {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Expected,
        [Parameter(Mandatory)][string]$Why
    )

    $script:checked++
    $normalised = [regex]::Replace($Expected, "\s+", " ").Trim()
    if ($Source -notlike "*$normalised*") {
        $script:failures.Add("MISSING: $Why`n  expected to find: $normalised")
    }
}

function Assert-NotContains {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Unexpected,
        [Parameter(Mandatory)][string]$Why
    )

    $script:checked++
    if ($Source -like "*$Unexpected*") {
        $script:failures.Add("PRESENT: $Why`n  found: $Unexpected")
    }
}

# ---------------------------------------------------------------------------
# 1. The game side: does vanilla still have the shape the adapter relies on?
# ---------------------------------------------------------------------------

$fireplace = Get-TypeSource -TypeName "Fireplace"

Assert-Contains $fireplace "public bool UseItem(Humanoid user, ItemDrop.ItemData item)" `
    "UseItem is the only member that consumes a real item for the fuel it adds"
Assert-Contains $fireplace "inventory.RemoveItem(item, 1);" `
    "UseItem takes exactly one item out of the user's own inventory"
Assert-Contains $fireplace 'm_nview.InvokeRPC("RPC_AddFuel");' `
    "and then invokes vanilla's own owner-gated add"
Assert-Contains $fireplace "if ((float)Mathf.CeilToInt(m_nview.GetZDO().GetFloat(ZDOVars.s_fuel)) >= m_maxFuel)" `
    "'full' is a CEILING comparison; writing it as fuel >= maxFuel fetches wood a fire will refuse"
Assert-Contains $fireplace "private void RPC_AddFuel(long sender) { if (m_nview.IsOwner())" `
    "RPC_AddFuel is owner-gated, which is why an UNOWNED object takes the item and discards the fuel"
Assert-Contains $fireplace "public ItemDrop m_fuelItem;" `
    "what a fire burns is read off the fire, never assumed to be wood"
Assert-Contains $fireplace "public float m_maxFuel" `
    "capacity is a float on the component"
Assert-Contains $fireplace "public bool m_canRefill" `
    "a fire that takes no fuel says so"
Assert-Contains $fireplace "public bool m_infiniteFuel;" `
    "a fire that never runs out says so"
Assert-Contains $fireplace "public void AddFuel(float fuel)" `
    "AddFuel still exists and is still forbidden: it adds fuel with no item consumed"
Assert-Contains $fireplace "public void SetFuel(float fuel)" `
    "SetFuel still exists and is still forbidden, for the same reason"
Assert-Contains $fireplace "m_nview.ClaimOwnership();" `
    "Interact still claims ownership, which is why the adapter uses UseItem instead"
Assert-NotContains $fireplace "Vector3.Distance" `
    "Fireplace still has NO distance test of its own, so the adapter must enforce reach itself"

$routed = Get-TypeSource -TypeName "ZRoutedRpc"
Assert-Contains $routed "if (targetPeerID == m_id || targetPeerID == 0L) { HandleRoutedRPC(routedRPCData); }" `
    "a routed RPC still runs INLINE when this peer is the target, which is what makes the fuel delta measurable"

$inventory = Get-TypeSource -TypeName "Inventory"
Assert-Contains $inventory "public int CountItems(string name, int quality = -1, bool matchWorldLevel = true)" `
    "CountItems still filters on world level while UseItem does not, so the fuel path must not use it"
Assert-Contains $inventory "public void MoveItemToThis(Inventory fromInventory, ItemDrop.ItemData item)" `
    "vanilla's own move is still add-then-remove inside one call"
Assert-Contains $inventory "public List<ItemDrop.ItemData> GetAllItems()" `
    "the adapter counts by enumerating and matching the shared name, as UseItem does"

$character = Get-TypeSource -TypeName "Character"
Assert-Contains $character "public virtual void Message(MessageHud.MessageType type, string msg, int amount = 0, Sprite icon = null, bool log = false) { }" `
    "Character.Message is still an EMPTY virtual, so UseItem's user.Message calls are inert on a worker body"

$piece = Get-TypeSource -TypeName "Piece"
Assert-Contains $piece "public static void GetAllPiecesInRadius(Vector3 p, float radius, List<Piece> pieces)" `
    "the survey still walks placed pieces rather than sweeping the world"

$container = Get-TypeSource -TypeName "Container"
Assert-Contains $container "public Inventory GetInventory()" "the depot's inventory is reachable"
Assert-Contains $container "public bool IsInUse()" "a chest a player has open still says so"
Assert-Contains $container "public bool IsOwner()" "a chest this session does not own still says so"

$zdoMan = Get-TypeSource -TypeName "ZDOMan"
Assert-Contains $zdoMan "public bool GetAllZDOsWithPrefabIterative(string prefab, List<ZDO> zdos, ref int index)" `
    "the census still counts saved bodies, loaded or not"

# ---------------------------------------------------------------------------
# 2. The product side: does the SHIPPED DLL avoid every forbidden path?
# ---------------------------------------------------------------------------

$shipped = (& ilspycmd --disable-updatecheck -r $managed $stewardDll | Out-String)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($shipped)) {
    throw "Could not decompile the built Steward DLL."
}

$forbidden = @(
    @{ Token = ".AddFuel("; Why = "conjures fuel with no item consumed" },
    @{ Token = ".SetFuel("; Why = "conjures fuel with no item consumed" },
    @{ Token = "RPC_AddFuelAmount"; Why = "the RPC behind AddFuel" },
    @{ Token = "RPC_SetFuelAmount"; Why = "the RPC behind SetFuel" },
    @{ Token = "ClaimOwnership"; Why = "seizes ownership of an object this session was not granted" },
    @{ Token = ".SetOwner("; Why = "seizes ownership one level down" },
    @{ Token = ".AddForce("; Why = "writes physics to a body" },
    @{ Token = ".AddTorque("; Why = "writes physics to a body" },
    @{ Token = ".velocity ="; Why = "writes a velocity" },
    @{ Token = ".position ="; Why = "writes a transform" },
    @{ Token = ".rotation ="; Why = "writes a transform" },
    @{ Token = "TeleportTo"; Why = "teleports" },
    @{ Token = "HarmonyPatch"; Why = "patches the game; this plugin patches nothing" },
    @{ Token = ".CountItems("; Why = "counts with a predicate vanilla does not mutate with" },
    @{ Token = ".HaveItem("; Why = "counts with a predicate vanilla does not mutate with" }
)

foreach ($entry in $forbidden) {
    Assert-NotContains $shipped $entry.Token "the shipped DLL calls $($entry.Token), which $($entry.Why)"
}

Assert-Contains $shipped "UseItem(" `
    "the shipped DLL does reach vanilla's one legitimate fuel path"
Assert-Contains $shipped "GetAllPiecesInRadius" `
    "the shipped DLL surveys placed pieces"

# ---------------------------------------------------------------------------

if ($failures.Count -gt 0) {
    Write-Host ""
    foreach ($failure in $failures) {
        Write-Host $failure -ForegroundColor Red
    }

    throw "Steward fire-API audit FAILED: $($failures.Count) of $checked checks. The adapter is written against a shape this game build no longer has, or the DLL gained a forbidden call."
}

Write-Host "Steward fire-API audit passed: $checked checks against assembly_valheim.dll $hash and the built DLL."
