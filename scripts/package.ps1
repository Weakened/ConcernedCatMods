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

# Read the version to expect directly from the csproj that was just built,
# so an RC seal's --expected-version check is enforced on every package
# build automatically instead of relying on someone typing it by hand.
$csprojPath = Join-Path $root "src" $Product "$Product.csproj"
[xml]$csprojXml = Get-Content $csprojPath
$expectedVersion = $csprojXml.Project.PropertyGroup |
    ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $expectedVersion) { throw "Could not read <Version> from $csprojPath." }

Push-Location $root
try {
    python ./tools/validate_repo.py --product $productSlug --require-binary --expected-version $expectedVersion
    if ($LASTEXITCODE -ne 0) { throw "Repository/package validation failed." }

    tcli build --config-path ./src/$Product/Package/thunderstore.toml
    if ($LASTEXITCODE -ne 0) { throw "TCLI package build failed." }

    # TCLI stamps every entry with the build time. Canonicalize the completed
    # archive so identical source payloads produce identical release bytes.
    $packagePath = Join-Path $root "artifacts" "thunderstore" "TheConcernedCat-$Product-$expectedVersion.zip"
    python ./tools/reproducible_zip.py $packagePath
    if ($LASTEXITCODE -ne 0) { throw "Reproducible ZIP canonicalization failed." }

    Write-Host "Package created under artifacts\thunderstore. Import it into a fresh mod-manager profile before publishing."
}
finally {
    Pop-Location
}
