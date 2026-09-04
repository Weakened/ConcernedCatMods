[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipBuild,
    [ValidateSet("ConcernedCartographer", "ConcernedTeamster")]
    [string]$Product = "ConcernedCartographer"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "RepoTools.psm1") -Force

$root = Get-RepoRoot
$environment = Get-EnvironmentValues -Root $root

# Each product deploys to its own dedicated mod-manager profile so testing
# one mod never contaminates the other's evidence (TCC-Dev vs TCT-Dev).
if ($Product -eq "ConcernedTeamster") {
    if ([string]::IsNullOrWhiteSpace($environment.TeamsterDeployPath)) {
        throw "TEAMSTER_DEPLOYPATH is not configured in Environment.props. Copy the block from Environment.props.example and point it at the TCT-Dev profile's plugins folder."
    }
    Assert-PathValue -Name "TEAMSTER_DEPLOYPATH" -Path $environment.TeamsterDeployPath
    $deployRoot = $environment.TeamsterDeployPath
    $profileName = "TCT-Dev"
} else {
    Assert-PathValue -Name "MOD_DEPLOYPATH" -Path $environment.ModDeployPath
    $deployRoot = $environment.ModDeployPath
    $profileName = "TCC-Dev"
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -Product $Product
}

$dllName = "TheConcernedCat.$Product.dll"
$output = Join-Path $root "src\$Product\bin\$Configuration\net48"
$dll = Join-Path $output $dllName
if (-not (Test-Path $dll)) {
    throw "Compiled DLL was not found: $dll"
}

$destination = Join-Path $deployRoot $Product
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item $dll $destination -Force

$pdb = Join-Path $output ($dllName -replace '\.dll$', '.pdb')
if (Test-Path $pdb) {
    Copy-Item $pdb $destination -Force
}

Write-Host "Deployed $Product to: $destination"
Write-Host "Launch the $profileName profile with Start modded, then inspect BepInEx\LogOutput.log."
