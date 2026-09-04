[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    # Default builds the whole solution (both products), matching historical
    # behavior now that the solution contains Concerned Teamster too.
    [ValidateSet("All", "ConcernedCartographer", "ConcernedTeamster")]
    [string]$Product = "All"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "RepoTools.psm1") -Force

$root = Get-RepoRoot
Assert-Command dotnet
$environment = Get-EnvironmentValues -Root $root
Assert-PathValue -Name "VALHEIM_INSTALL" -Path $environment.ValheimInstall
Assert-PathValue -Name "BEPINEX_PATH" -Path $environment.BepInExPath

Push-Location $root
try {
    if ($Product -eq "All") {
        dotnet build ./ConcernedCatMods.sln --configuration $Configuration --nologo
    } else {
        # Jotunn's NuGet targets import $(SolutionDir)Environment.props, which
        # dotnet leaves undefined for direct csproj builds - pass it explicitly
        # so single-product builds see the same game paths as solution builds.
        $solutionDir = $root + [System.IO.Path]::DirectorySeparatorChar
        dotnet build "./src/$Product/$Product.csproj" --configuration $Configuration --nologo "-p:SolutionDir=$solutionDir"
    }
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}
finally {
    Pop-Location
}
