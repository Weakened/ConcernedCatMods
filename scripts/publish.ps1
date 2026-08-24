[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
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

Push-Location $root
try {
    python ./tools/validate_repo.py --expected-version $Version
    if ($LASTEXITCODE -ne 0) { throw "Version validation failed." }

    & (Join-Path $PSScriptRoot "package.ps1") -Configuration Release

    $confirmation = Read-Host "Type PUBLISH $Version to upload TheConcernedCat-ConcernedCartographer"
    if ($confirmation -ne "PUBLISH $Version") {
        throw "Publish cancelled."
    }

    tcli publish --config-path ./src/ConcernedCartographer/Package/thunderstore.toml
    if ($LASTEXITCODE -ne 0) { throw "TCLI publish failed." }

    Write-Host "Published Concerned Cartographer $Version. Create the tag concerned-cartographer/v$Version after verifying the listing."
}
finally {
    Pop-Location
}
