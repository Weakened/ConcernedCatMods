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
    & (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -Product ConcernedTeamster
}

$managed = Join-Path $environment.ValheimInstall "valheim_Data\Managed"
$gameAssembly = Join-Path $managed "assembly_valheim.dll"
$globalManagers = Join-Path $environment.ValheimInstall "valheim_Data\globalgamemanagers"
$output = Join-Path $root "src\ConcernedTeamster\bin\$Configuration\net48"
$teamsterDll = Join-Path $output "TheConcernedCat.ConcernedTeamster.dll"
$publicizedAssembly = Join-Path $output "assembly_valheim_publicized.dll"
$bepInExDll = Join-Path $environment.BepInExPath "core\BepInEx.dll"
$jotunnDll = Join-Path $environment.BepInExPath "plugins\ValheimModding-Jotunn\Jotunn.dll"

foreach ($required in @($gameAssembly, $globalManagers, $teamsterDll, $publicizedAssembly, $bepInExDll, $jotunnDll)) {
    if (-not (Test-Path $required -PathType Leaf)) {
        throw "Required audit input is missing: $required"
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

$versionSource = (& ilspycmd -t Version $gameAssembly | Out-String)
if ($LASTEXITCODE -ne 0) { throw "Could not inspect Valheim Version metadata." }
$versionMatch = [regex]::Match($versionSource, 'CurrentVersion\s*\{\s*get;\s*\}\s*=\s*new GameVersion\((\d+),\s*(\d+),\s*(\d+)\)')
if (-not $versionMatch.Success) { throw "Could not resolve Valheim GameVersion from assembly_valheim.dll." }
$gameVersion = '{0}.{1}.{2}' -f $versionMatch.Groups[1].Value, $versionMatch.Groups[2].Value, $versionMatch.Groups[3].Value

$managerText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($globalManagers))
$unityMatch = [regex]::Match($managerText, '6000\.0\.\d+f\d+')
if (-not $unityMatch.Success) { throw "Could not resolve the Unity 6000 version." }

$characterSource = (& ilspycmd -t Character $gameAssembly | Out-String)
if ($LASTEXITCODE -ne 0) { throw "Could not inspect Character metadata." }
$normalizedCharacter = [regex]::Replace($characterSource, '\s+', ' ')
$currentMessage = 'public virtual void Message(MessageHud.MessageType type, string msg, int amount = 0, Sprite icon = null, bool log = false)'
if (-not $normalizedCharacter.Contains($currentMessage)) {
    throw "Character.Message does not match the audited five-argument Valheim 1.0.7 contract."
}

$teamsterIl = (& ilspycmd -il $teamsterDll | Out-String)
if ($LASTEXITCODE -ne 0) { throw "Could not inspect the Teamster IL." }
$messageReferenceCount = ([regex]::Matches($teamsterIl, 'Character::Message')).Count
if ($messageReferenceCount -ne 0) {
    throw "Teamster contains $messageReferenceCount compiled Character.Message reference(s)."
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
    characterMessageParameterCount = 5
    teamsterCharacterMessageReferences = $messageReferenceCount
    gameAssembly = Get-FileIdentity $gameAssembly
    publicizedReference = Get-FileIdentity $publicizedAssembly
    bepinex = Get-FileIdentity $bepInExDll
    jotunn = Get-FileIdentity $jotunnDll
    teamster = Get-FileIdentity $teamsterDll
}

$result | ConvertTo-Json -Depth 4
Write-Host "Teamster game-API compatibility audit PASS."
