[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipBuild,
    [ValidateSet("ConcernedCartographer", "ConcernedTeamster")]
    [string]$Product = "ConcernedCartographer",
    # CT-043: which Teamster profile family member to deploy to. Ignored for
    # ConcernedCartographer (ConcernedCartographer always deploys to TCC-Dev;
    # it has no profile family of its own here). TCT-Clean is deliberately
    # not a valid value — it exists specifically WITHOUT Teamster installed,
    # as the vanilla-truth baseline every other profile is compared against,
    # so nothing ever deploys there.
    [ValidateSet("Dev", "Compat", "Dedicated")]
    [string]$Profile = "Dev"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "RepoTools.psm1") -Force

$root = Get-RepoRoot
$environment = Get-EnvironmentValues -Root $root

# Each product deploys to its own dedicated mod-manager profile so testing
# one mod never contaminates the other's evidence (TCC-Dev vs the TCT
# family). Within the TCT family, -Profile picks which named profile's
# plugins folder receives this build — see Environment.props.example for
# how each one is set up.
if ($Product -eq "ConcernedTeamster") {
    $profileConfig = @{
        Dev       = @{ Path = $environment.TeamsterDeployPath; Key = "TEAMSTER_DEPLOYPATH"; Name = "TCT-Dev" }
        Compat    = @{ Path = $environment.TeamsterCompatDeployPath; Key = "TEAMSTER_COMPAT_DEPLOYPATH"; Name = "TCT-Compat" }
        Dedicated = @{ Path = $environment.TeamsterDedicatedDeployPath; Key = "TEAMSTER_DEDICATED_DEPLOYPATH"; Name = "TCT-Dedicated" }
    }[$Profile]

    if ([string]::IsNullOrWhiteSpace($profileConfig.Path)) {
        throw "$($profileConfig.Key) is not configured in Environment.props. Copy the block from Environment.props.example and point it at the $($profileConfig.Name) profile's plugins folder."
    }
    Assert-PathValue -Name $profileConfig.Key -Path $profileConfig.Path
    $deployRoot = $profileConfig.Path
    $profileName = $profileConfig.Name
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
