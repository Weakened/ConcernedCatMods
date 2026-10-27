# Export the saved LadderGait.blend and regenerate GeneratedGait.cs (plus ladder_gait.json and frames/*.csv).
# Optional static-value edits are applied to the .blend first, e.g.
#   pwsh tools/animation/gait/regenerate.ps1 CTL_Bend.knee_toward=0.3 CTL_Gait.hand_mid_from_shoulder=0.0
# Use -Check to only verify that GeneratedGait.cs, ladder_gait.json and LadderGait.blend agree.
param([switch]$Check, [Parameter(ValueFromRemainingArguments = $true)][string[]]$Edits)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$blender = if ($env:BLENDER) { $env:BLENDER } else { 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' }
$blend = Join-Path $root 'src\ConcernedLadders\AnimationSources\LadderGait.blend'
$compile = Join-Path $PSScriptRoot 'compile_gait.py'
if ($Check) {
    python $compile --check
    if ($LASTEXITCODE -ne 0) { throw 'generated gait is stale' }
    return
}
if (-not (Test-Path -LiteralPath $blender)) { throw "Blender not found at $blender (set `$env:BLENDER)" }
if ($Edits.Count -gt 0) {
    & $blender --background $blend --python-exit-code 1 --python (Join-Path $PSScriptRoot 'set_props.py') -- @Edits | Select-String '^set |Error'
    if ($LASTEXITCODE -ne 0) { throw 'property edit failed' }
}
& $blender --background $blend --python-exit-code 1 --python (Join-Path $PSScriptRoot 'export_gait.py') | Select-String 'exported|Error'
if ($LASTEXITCODE -ne 0) { throw 'export failed' }
python $compile
if ($LASTEXITCODE -ne 0) { throw 'compile failed' }
python $compile --check
if ($LASTEXITCODE -ne 0) { throw 'check failed after compiling' }
Remove-Item -LiteralPath "$blend`1" -ErrorAction SilentlyContinue
