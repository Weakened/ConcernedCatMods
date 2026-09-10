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
foreach ($required in @($gameAssembly, $resources, $globalManagers, $bepInExDll, $jotunnDll)) {
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
        throw "Valheim 1.0.7 ship contract moved: $Contract"
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
Assert-SourceContains $player 'StopDoodadControl();' "vanilla helm-release lifecycle"
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
$result = [ordered]@{
    result = "PASS"
    valheimVersion = $gameVersion
    steamBuildId = $steamBuildId
    unityVersion = $unityMatch.Value
    vessels = $vessels
    hookBoundary = "ShipControlls.ApplyControlls(Vector3, Vector3, bool, bool, bool)"
    controllerIdentity = "Player.GetControlledShip + ShipControlls.GetUser/HaveValidUser"
    simulationAuthority = "Ship.ZNetView owner only"
    rudderTransport = "vanilla owner-targeted Rudder RPC; owner publishes ZDO rudder"
    sailPolicy = "read-only; vanilla speed state and EnvMan wind remain authoritative"
    movementReplication = "vanilla ZSyncTransform owner position/rotation"
    gameAssembly = Get-FileIdentity $gameAssembly
    resources = Get-FileIdentity $resources
    globalManagers = Get-FileIdentity $globalManagers
    bepinex = Get-FileIdentity $bepInExDll
    jotunn = Get-FileIdentity $jotunnDll
}

$result | ConvertTo-Json -Depth 5
Write-Host "Cartographer ship-control compatibility audit PASS."
