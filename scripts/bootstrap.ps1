[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "RepoTools.psm1") -Force

$root = Get-RepoRoot
Assert-Command git
Assert-Command dotnet
Assert-Command python

$environment = Get-EnvironmentValues -Root $root
Assert-PathValue -Name "VALHEIM_INSTALL" -Path $environment.ValheimInstall
Assert-PathValue -Name "BEPINEX_PATH" -Path $environment.BepInExPath
Assert-PathValue -Name "MOD_DEPLOYPATH" -Path $environment.ModDeployPath

$managed = Join-Path $environment.ValheimInstall "valheim_Data\Managed"
if (-not (Test-Path $managed)) {
    $managed = Join-Path $environment.ValheimInstall "Valheim_Data\Managed"
}
Assert-PathValue -Name "Valheim managed assemblies" -Path $managed
Assert-PathValue -Name "BepInEx core" -Path (Join-Path $environment.BepInExPath "core")

Push-Location $root
try {
    Write-Host "Running repository metadata validation..."
    python ./tools/validate_repo.py

    Write-Host "Restoring NuGet packages..."
    dotnet restore ./ConcernedCatMods.sln
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

    Write-Host "Bootstrap complete. The first build will run Jotunn's publicizer against your local Valheim installation."
}
finally {
    Pop-Location
}
