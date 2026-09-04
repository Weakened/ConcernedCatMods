[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release",
    [ValidateSet("ConcernedCartographer", "ConcernedTeamster")]
    [string]$Product = "ConcernedCartographer"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "RepoTools.psm1") -Force

$root = Get-RepoRoot
Assert-Command python
Assert-Command tcli

& (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -Product $Product

$productSlug = @{
    ConcernedCartographer = "cartographer"
    ConcernedTeamster     = "teamster"
}[$Product]

Push-Location $root
try {
    python ./tools/validate_repo.py --product $productSlug --require-binary
    if ($LASTEXITCODE -ne 0) { throw "Repository/package validation failed." }

    tcli build --config-path ./src/$Product/Package/thunderstore.toml
    if ($LASTEXITCODE -ne 0) { throw "TCLI package build failed." }

    Write-Host "Package created under artifacts\thunderstore. Import it into a fresh mod-manager profile before publishing."
}
finally {
    Pop-Location
}
