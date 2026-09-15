[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "RepoTools.psm1") -Force

$root = Get-RepoRoot
$environment = Get-EnvironmentValues -Root $root
Assert-Command ilspycmd
Assert-PathValue -Name "VALHEIM_INSTALL" -Path $environment.ValheimInstall
Assert-PathValue -Name "BEPINEX_PATH" -Path $environment.BepInExPath

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -Product ConcernedCartographer
}

$managed = Join-Path $environment.ValheimInstall "valheim_Data\Managed"
$gameAssembly = Join-Path $managed "assembly_valheim.dll"
$resources = Join-Path $environment.ValheimInstall "valheim_Data\resources.assets"
$globalManagers = Join-Path $environment.ValheimInstall "valheim_Data\globalgamemanagers"
$bepInExDll = Join-Path $environment.BepInExPath "core\BepInEx.dll"
$jotunnDll = Join-Path $environment.BepInExPath "plugins\ValheimModding-Jotunn\Jotunn.dll"
$contractPath = Join-Path $root "docs\mods\concerned-cartographer\SHIP_CONTROL_COMPATIBILITY.md"
$adapterPath = Join-Path $root "src\ConcernedCartographer\Runtime\SailingRouteFollowAdapter.cs"
$sailingControllerPath = Join-Path $root "src\ConcernedCartographer\Domain\Atlas\SailingRouteFollowController.cs"
foreach ($required in @($gameAssembly, $resources, $globalManagers, $bepInExDll, $jotunnDll, $contractPath)) {
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
        throw "Installed Valheim ship contract moved: $Contract"
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
            throw "Installed Valheim ship contract moved: $Contract (missing or out of order: $needle)"
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

$contractSource = Get-Content -LiteralPath $contractPath -Raw
foreach ($contractNeedle in @(
    'A read-only prefix on `Player.SetControls`',
    'A prefix on `ShipControlls.ApplyControlls`',
    'A prefix on `Player.StopDoodadControl`',
    '`ApplyControlls`-only fallback is forbidden'
)) {
    Assert-SourceContains $contractSource $contractNeedle "documented raw-input, steering, and lifecycle seams"
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
$manifest = Join-Path (Split-Path $environment.ValheimInstall -Parent) "..\appmanifest_892970.acf"
$steamBuildId = $null
if (Test-Path $manifest) {
    $buildLine = Select-String -Path $manifest -Pattern '"buildid"\s+"(\d+)"' | Select-Object -First 1
    if ($null -ne $buildLine) {
        $steamBuildId = $buildLine.Matches[0].Groups[1].Value
    }
}

$managerText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($globalManagers))
$unityMatch = [regex]::Match($managerText, '6000\.0\.\d+f\d+')
if (-not $unityMatch.Success) {
    throw "Could not resolve the Unity 6000 version."
}

$ship = Get-TypeSource -TypeName "Ship"
$controls = Get-TypeSource -TypeName "ShipControlls"
$player = Get-TypeSource -TypeName "Player"
$zNetView = Get-TypeSource -TypeName "ZNetView"
$zSyncTransform = Get-TypeSource -TypeName "ZSyncTransform"

Assert-SourceContains $ship 'public void ApplyControlls(Vector3 dir)' "Ship.ApplyControlls(Vector3)"
Assert-SourceContains $ship 'm_nview.InvokeRPC("Rudder", m_rudderValue);' "rudder input must use the vanilla owner-targeted RPC"
Assert-SourceContains $ship 'if ((bool)m_nview && !m_nview.IsOwner()) { return; }' "ship force simulation must remain ZNetView-owner-only"
Assert-SourceContains $ship 'm_nview.GetZDO().Set(ZDOVars.s_forward, (int)m_speed);' "owner must publish the vanilla sail/speed setting"
Assert-SourceContains $ship 'm_nview.GetZDO().Set(ZDOVars.s_rudder, m_rudderValue);' "owner must publish the vanilla rudder value"
Assert-SourceContains $ship 'Vector3 windDir = EnvMan.instance.GetWindDir();' "sail force must continue reading vanilla wind direction"
Assert-SourceContains $ship 'float windIntensity = EnvMan.instance.GetWindIntensity();' "sail force must continue reading vanilla wind intensity"
foreach ($getter in @(
    'public bool IsSailUp()',
    'public float GetWindAngleFactor()',
    'public bool IsOwner()',
    'public Speed GetSpeedSetting()',
    'public float GetRudderValue()',
    'public static Ship GetLocalShip()'
)) {
    Assert-SourceContains $ship $getter "required read-only Ship member $getter"
}

Assert-SourceContains $controls 'public void ApplyControlls(Vector3 moveDir, Vector3 lookDir, bool run, bool autoRun, bool block)' "ShipControlls.ApplyControlls hook boundary"
Assert-SourceContains $controls 'm_ship.ApplyControlls(moveDir);' "ShipControlls must delegate through vanilla Ship.ApplyControlls"
Assert-SourceContains $controls 'if (m_nview.IsOwner() && m_ship.IsPlayerInBoat(playerID))' "helm grants must be validated by the ship network owner"
Assert-SourceContains $controls 'm_nview.GetZDO().Set(ZDOVars.s_user, playerID);' "granted helm user must remain vanilla ZDO state"
foreach ($member in @(
    'public bool HaveValidUser()',
    'public long GetUser()',
    'public void OnUseStop(Player player)'
)) {
    Assert-SourceContains $controls $member "required helm lifecycle member $member"
}
Assert-SourceContains $player 'm_doodadController.ApplyControlls(moveDir, lookDir, run, autoRun, block);' "Player must pass raw controls through the doodad controller"
Assert-SourceContains $player 'public Ship GetControlledShip()' "local-player controlled-ship identity"
Assert-SourceContains $player 'public IDoodadController GetDoodadController()' "local-player doodad-controller identity"
Assert-SourceContains $player 'public void SetControls(Vector3 movedir, bool attack, bool attackHold, bool secondaryAttack, bool secondaryAttackHold, bool block, bool blockHold, bool jump, bool crouch, bool run, bool autoRun, bool dodge = false)' "raw Player.SetControls observation boundary"
Assert-SourceOrder $player @(
    'public void SetControls(Vector3 movedir, bool attack, bool attackHold, bool secondaryAttack, bool secondaryAttackHold, bool block, bool blockHold, bool jump, bool crouch, bool run, bool autoRun, bool dodge = false)',
    'SetDoodadControlls(ref movedir, ref m_lookDir, ref run, ref autoRun, blockHold);',
    'if (jump | attack | secondaryAttack | dodge)',
    'StopDoodadControl();'
) "Player.SetControls currently dispatches doodad input before its helm-exit check, so raw exit input must be observed in a Player.SetControls prefix"
Assert-SourceOrder $player @(
    'public void StopDoodadControl()',
    'm_doodadController.OnUseStop(this);',
    'm_doodadController = null;'
) "Player.StopDoodadControl prefix must observe the exact controller before vanilla clears it"
Assert-SourceContains $zNetView 'ZRoutedRpc.instance.InvokeRoutedRPC(m_zdo.GetOwner(), m_zdo.m_uid, method, parameters);' "default ZNetView RPC target must remain the object's owner"
Assert-SourceContains $zSyncTransform 'zDO.SetPosition(position2);' "owner transform position replication"
Assert-SourceContains $zSyncTransform 'zDO.SetRotation(rotation);' "owner transform rotation replication"
Assert-SourceContains $zSyncTransform 'private void ClientSync(float dt)' "non-owner transform interpolation"

$resourceText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($resources))
$shipRows = [regex]::Matches($resourceText, '"ship_([^"]+)","([^"]+)"')
$displayRows = [ordered]@{}
foreach ($row in $shipRows) {
    $displayRows[$row.Groups[1].Value] = $row.Groups[2].Value
}

$expectedDisplayRows = [ordered]@{
    "raft" = "Raft"
    "karve" = "Karve"
    "longship" = "Longship"
    "longship_ashlands" = "Drakkar"
    "cargo" = "Cargo"
    "holdfast" = "Hold fast"
    "ashlandocean_warning" = "The sizzling waters are eating through your hull, turn back!"
}

foreach ($expected in $expectedDisplayRows.GetEnumerator()) {
    if (-not $displayRows.Contains($expected.Key) -or
        $displayRows[$expected.Key] -ne $expected.Value) {
        throw "Installed ship asset catalog changed at ship_$($expected.Key)."
    }
}
if ($displayRows.Count -ne $expectedDisplayRows.Count) {
    $actual = ($displayRows.Keys | Sort-Object) -join ", "
    throw "Installed ship asset catalog has an unreviewed display key: $actual"
}

$vessels = [ordered]@{
    raft = $displayRows["raft"]
    karve = $displayRows["karve"]
    longship = $displayRows["longship"]
    longship_ashlands = $displayRows["longship_ashlands"]
}
# --- #243: the shipped adapter must bind exactly the three audited seams --
# Static source evidence only. It proves the hook set and the forbidden-call
# set; it proves NOTHING about live Harmony ordering or multiplayer.
function Get-CodeOnlySource {
    # Comments explain the vanilla equations by name, so the forbidden-call
    # scan must read CODE, not documentation. A naive cut at the first '//'
    # would also amputate any line containing a URL inside a string literal,
    # so quotes are tracked.
    param([Parameter(Mandatory)][string]$Path)

    $stripped = foreach ($line in (Get-Content -LiteralPath $Path)) {
        $inString = $false
        $cut = -1
        for ($i = 0; $i -lt $line.Length; $i++) {
            $ch = $line[$i]
            if ($ch -eq '"' -and ($i -eq 0 -or $line[$i - 1] -ne '\')) {
                $inString = -not $inString
                continue
            }

            if (-not $inString -and $ch -eq '/' -and
                $i + 1 -lt $line.Length -and $line[$i + 1] -eq '/') {
                $cut = $i
                break
            }
        }

        if ($cut -ge 0) { $line.Substring(0, $cut) } else { $line }
    }

    return [regex]::Replace(($stripped -join " "), "\s+", " ").Trim()
}

$sailingHookEvidence = "not present"
if ((Test-Path $adapterPath -PathType Leaf) -and (Test-Path $sailingControllerPath -PathType Leaf)) {
    $adapterSource = Get-CodeOnlySource -Path $adapterPath
    $controllerSource = Get-CodeOnlySource -Path $sailingControllerPath

    foreach ($seam in @(
        'nameof(Player.SetControls), setControlsSignature',
        'nameof(ShipControlls.ApplyControlls), new[] { typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool), typeof(bool) }',
        'nameof(Player.StopDoodadControl), Type.EmptyTypes'
    )) {
        Assert-SourceContains $adapterSource $seam "sailing adapter must resolve the audited seam: $seam"
    }

    $patchCount = ([regex]::Matches($adapterSource, 'installing\.Patch\(')).Count
    if ($patchCount -ne 3) {
        throw "Sailing adapter installs $patchCount Harmony patches; the audited contract requires exactly 3."
    }

    Assert-SourceContains $adapterSource 'installing?.UnpatchSelf();' `
        "partial sailing hook installation must roll back"
    Assert-SourceContains $adapterSource 'moveDir = new Vector3(rudderInput, raw.y, raw.z);' `
        "steering may replace only the rudder axis"

    # Count, do not merely find: a substring match would be satisfied by ONE
    # prefix among three patches.
    $prefixCount = ([regex]::Matches($adapterSource, 'prefix: new HarmonyMethod\(')).Count
    if ($prefixCount -ne 3) {
        throw "Sailing adapter declares $prefixCount prefixes; all 3 seams must be prefixes."
    }

    # A transpiler or finalizer is strictly worse than a postfix here, and a
    # bool-returning prefix can skip the original whatever it returns.
    foreach ($modifier in @('postfix:', 'transpiler:', 'finalizer:', '__result', '__state')) {
        if ($adapterSource.Contains($modifier, [StringComparison]::Ordinal)) {
            throw "Sailing adapter must never skip or replace an original method (found: $modifier)."
        }
    }
    if ($adapterSource -match 'private static bool Before') {
        throw "Sailing adapter prefixes must return void; a bool prefix can skip the original."
    }

    # No absolute rudder write, no force/transform/ownership/sail control.
    # CartographerRuntime is included because that is where the sailing gates
    # actually touch Ship/ShipControlls; auditing only the two dedicated files
    # would leave the real integration point unscanned. Its sailing region is
    # isolated first so unrelated runtime code cannot trip the scan.
    $runtimePath = Join-Path $root "src\ConcernedCartographer\Runtime\CartographerRuntime.cs"
    $runtimeSailing = ""
    if (Test-Path $runtimePath -PathType Leaf) {
        $runtimeLines = Get-Content -LiteralPath $runtimePath
        $collecting = $false
        $collected = foreach ($line in $runtimeLines) {
            if ($line -match 'private (void|bool|SailingRouteFollowFrame) (HandleSailing|BuildSailingFrame|TryStartSailingRouteFollow|StopSailingRouteFollow|ReportSailingStopped)') {
                $collecting = $true
            } elseif ($line -match '^    private .*Walking' -or $line -match '^    public void Tick\(') {
                $collecting = $false
            }

            if ($collecting) { $line }
        }

        $runtimeSailing = [regex]::Replace(($collected -join " "), "\s+", " ").Trim()
        if ($runtimeSailing.Length -lt 500) {
            throw "Could not isolate the sailing region of CartographerRuntime.cs for the forbidden-call scan."
        }
    }

    foreach ($forbidden in @(
        'm_rudderValue', 'Rudder(', 'SetOwner', 'ClaimOwnership', 'AddForce',
        'velocity =', 'transform.position =', 'transform.rotation =',
        '.Forward()', '.Backward()', 'InvokeRPC', 'GetWindDir', 'SetWind',
        'm_body', 'AddTorque', 'MovePosition', 'MoveRotation'
    )) {
        foreach ($sailingSource in @($adapterSource, $controllerSource, $runtimeSailing)) {
            if ($sailingSource.Contains($forbidden, [StringComparison]::Ordinal)) {
                throw "Sailing Route Follow source contains a forbidden call: $forbidden"
            }
        }
    }

    $sailingHookEvidence = "3 prefixes (Player.SetControls, ShipControlls.ApplyControlls, Player.StopDoodadControl), transactional rollback, rudder-axis-only write, no forbidden call in the adapter, the controller, or the runtime sailing region"
}

$result = [ordered]@{
    result = "PASS"
    valheimVersion = $gameVersion
    steamBuildId = $steamBuildId
    unityVersion = $unityMatch.Value
    vessels = $vessels
    rawInputObservation = "read-only Player.SetControls prefix before doodad dispatch"
    steeringInjection = "ShipControlls.ApplyControlls prefix after raw-input cancellation"
    helmExitOrdering = "installed Player.SetControls dispatches doodad controls before jump/attack/secondary/dodge stop"
    lifecycleCancellation = "Player.StopDoodadControl prefix before OnUseStop and controller clear"
    controllerIdentity = "Player.GetControlledShip + Player.GetDoodadController + ShipControlls.GetUser/HaveValidUser"
    simulationAuthority = "Ship.ZNetView owner only"
    rudderTransport = "vanilla owner-targeted Rudder RPC; owner publishes ZDO rudder"
    sailPolicy = "read-only; vanilla speed state and EnvMan wind remain authoritative"
    sailingAdapterStaticAudit = $sailingHookEvidence
    movementReplication = "vanilla ZSyncTransform owner position/rotation"
    gameAssembly = Get-FileIdentity $gameAssembly
    resources = Get-FileIdentity $resources
    globalManagers = Get-FileIdentity $globalManagers
    bepinex = Get-FileIdentity $bepInExDll
    jotunn = Get-FileIdentity $jotunnDll
}

$result | ConvertTo-Json -Depth 5
Write-Host "Cartographer ship-control compatibility audit PASS."
