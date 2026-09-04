Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Get-EnvironmentValues {
    param([string]$Root = (Get-RepoRoot))

    $path = Join-Path $Root "Environment.props"
    if (-not (Test-Path $path)) {
        throw "Environment.props is missing. Copy Environment.props.example to Environment.props and edit the paths."
    }

    [xml]$xml = Get-Content -Raw $path
    $group = $xml.Project.PropertyGroup | Select-Object -First 1
    if ($null -eq $group) {
        throw "Environment.props does not contain a PropertyGroup."
    }

    $valheim = [string]$group.VALHEIM_INSTALL
    $bepInEx = [string]$group.BEPINEX_PATH
    $deploy = [string]$group.MOD_DEPLOYPATH

    # Optional (added with Concerned Teamster): older Environment.props files
    # legitimately omit it, so read it without tripping strict mode.
    $teamsterNode = $group.SelectSingleNode("TEAMSTER_DEPLOYPATH")
    $teamsterDeploy = if ($null -ne $teamsterNode) { [string]$teamsterNode.InnerText } else { "" }

    $bepInEx = $bepInEx.Replace('$(VALHEIM_INSTALL)', $valheim)
    $deploy = $deploy.Replace('$(VALHEIM_INSTALL)', $valheim).Replace('$(BEPINEX_PATH)', $bepInEx)
    $teamsterDeploy = $teamsterDeploy.Replace('$(VALHEIM_INSTALL)', $valheim)

    return [pscustomobject]@{
        ValheimInstall = $valheim
        BepInExPath = $bepInEx
        ModDeployPath = $deploy
        TeamsterDeployPath = $teamsterDeploy
    }
}

function Assert-Command {
    param([Parameter(Mandatory)][string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

function Assert-PathValue {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -match 'REPLACE_ME') {
        throw "$Name is not configured in Environment.props."
    }

    if (-not (Test-Path $Path)) {
        throw "$Name does not exist: $Path"
    }
}

Export-ModuleMember -Function Get-RepoRoot, Get-EnvironmentValues, Assert-Command, Assert-PathValue
