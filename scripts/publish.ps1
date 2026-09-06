[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [ValidateSet("ConcernedCartographer", "ConcernedTeamster")]
    [string]$Product = "ConcernedCartographer"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "RepoTools.psm1") -Force

$root = Get-RepoRoot
Assert-Command tcli
Assert-Command python

if ([string]::IsNullOrWhiteSpace($env:TCLI_AUTH_TOKEN)) {
    throw "TCLI_AUTH_TOKEN is not set in this PowerShell process."
}

$productSlug = @{
    ConcernedCartographer = "cartographer"
    ConcernedTeamster     = "teamster"
}[$Product]

Push-Location $root
try {
    python ./tools/validate_repo.py --product $productSlug --expected-version $Version
    if ($LASTEXITCODE -ne 0) { throw "Version validation failed." }

    & (Join-Path $PSScriptRoot "package.ps1") -Configuration Release -Product $Product

    $confirmation = Read-Host "Type PUBLISH $Version to upload TheConcernedCat-$Product"
    if ($confirmation -ne "PUBLISH $Version") {
        throw "Publish cancelled."
    }

    tcli publish --config-path ./src/$Product/Package/thunderstore.toml
    if ($LASTEXITCODE -ne 0) { throw "TCLI publish failed." }

    $tagPrefix = @{
        ConcernedCartographer = "concerned-cartographer"
        ConcernedTeamster     = "concerned-teamster"
    }[$Product]
    Write-Host "Published $Product $Version. Create the tag $tagPrefix/v$Version after verifying the listing."
}
finally {
    Pop-Location
}
