[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "RepoTools.psm1") -Force

$root = Get-RepoRoot
$environment = Get-EnvironmentValues -Root $root
Assert-PathValue -Name "MOD_DEPLOYPATH" -Path $environment.ModDeployPath

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration
}

$output = Join-Path $root "src\ConcernedCartographer\bin\$Configuration\net48"
$dll = Join-Path $output "TheConcernedCat.ConcernedCartographer.dll"
if (-not (Test-Path $dll)) {
    throw "Compiled DLL was not found: $dll"
}

$destination = Join-Path $environment.ModDeployPath "ConcernedCartographer"
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item $dll $destination -Force

$pdb = Join-Path $output "TheConcernedCat.ConcernedCartographer.pdb"
if (Test-Path $pdb) {
    Copy-Item $pdb $destination -Force
}

Write-Host "Deployed Concerned Cartographer to: $destination"
Write-Host "Launch the TCC-Dev profile with Start modded, then inspect BepInEx\LogOutput.log."
