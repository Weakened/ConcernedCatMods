# Generates deterministic Concerned Cartographer test fixtures into a
# profile's sidecar folder (CC-057). Never touches world saves.
#
#   pwsh ./scripts/make-test-fixtures.ps1 -ProfileConfigDir "<...>\BepInEx\config" -WorldUid 12345 [-Pins 1000] [-RoadKilometers 10]
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ProfileConfigDir,
    [Parameter(Mandatory)][long]$WorldUid,
    [int]$Pins = 1000,
    [int]$RoadKilometers = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$dir = Join-Path $ProfileConfigDir "ConcernedCatMods\ConcernedCartographer"
New-Item -ItemType Directory -Force $dir | Out-Null

# --- Roads: a deterministic spiral in the shipped v1 row format (also
# exercises migration + maintenance compaction on load). ---
$roadLines = [System.Collections.Generic.List[string]]::new()
$roadLines.Add("# ConcernedCartographer roads v1")
$points = [Math]::Max(10, [int]($RoadKilometers * 1000 / 15))
$strokeId = [guid]::NewGuid().ToString("D")
$culture = [System.Globalization.CultureInfo]::InvariantCulture
for ($i = 0; $i -lt $points; $i++) {
    if ($i % 200 -eq 0 -and $i -gt 0) { $strokeId = [guid]::NewGuid().ToString("D") }
    $angle = 0.35 * [Math]::Sqrt($i * 40.0)
    $radius = 25.0 + 2.2 * $angle
    $x = ($radius * [Math]::Cos($angle)).ToString("0.##", $culture)
    $z = ($radius * [Math]::Sin($angle)).ToString("0.##", $culture)
    $index = ($i % 200).ToString($culture)
    $roadLines.Add("$strokeId`tDirt`t$index`t$x`t31.25`t$z`t1")
}
Set-Content -Path (Join-Path $dir "$WorldUid.roads.tsv") -Value $roadLines
Write-Host "roads: $points points ($RoadKilometers km) in v1 format"

# --- Pins: deterministic grid in the current v2 row format. ---
$pinLines = [System.Collections.Generic.List[string]]::new()
$pinLines.Add("# ConcernedCartographer pins v2")
$now = [DateTime]::UtcNow.Ticks.ToString($culture)
$icons = @("vanilla:fire","vanilla:house","vanilla:hammer","vanilla:dot","vanilla:portal","cc:resource","cc:harbor","cc:danger")
for ($i = 0; $i -lt $Pins; $i++) {
    $id = "cc:pin:" + [guid]::NewGuid().ToString("N")
    $x = (($i % 100) * 55 - 2750).ToString($culture)
    $z = ((([int]($i / 100)) * 55 - 2750)).ToString($culture)
    $icon = $icons[$i % $icons.Count]
    $tag = if ($i % 2 -eq 0) { "even" } else { "odd" }
    $pinLines.Add("$id`t1`t$now`t$now`tFixture pin $i`t$icon`tFixtures`t`t1`t`t$tag`t0`t0`t1`t1`t0`t0`t`t`t`t$x`t31.25`t$z`t2")
}
Set-Content -Path (Join-Path $dir "$WorldUid.pins.tsv") -Value $pinLines
Write-Host "pins: $Pins in v2 format"

Write-Host "Fixtures written to $dir — load world $WorldUid to exercise migration, maintenance, clustering, and search at scale."
