# Export the saved LadderGait.blend and regenerate GeneratedGait.cs. Optional property edits are applied first, e.g.
#   pwsh tools/animation/gait/regenerate.ps1 HINTS.knee_side=-0.02 GAIT.hand_mid_from_shoulder=0.1
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$blender = 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe'
$blend = Join-Path $root 'src\ConcernedLadders\AnimationSources\LadderGait.blend'
if ($args.Count -gt 0) {
    & $blender --background $blend --python-exit-code 1 --python (Join-Path $PSScriptRoot 'set_props.py') -- @args | Select-String '^set |Error'
    if ($LASTEXITCODE -ne 0) { throw 'property edit failed' }
}
& $blender --background $blend --python-exit-code 1 --python (Join-Path $PSScriptRoot 'export_gait.py') | Select-String 'exported|Error'
if ($LASTEXITCODE -ne 0) { throw 'export failed' }
python (Join-Path $PSScriptRoot 'compile_gait.py')
if ($LASTEXITCODE -ne 0) { throw 'compile failed' }
Remove-Item -LiteralPath "$blend`1" -ErrorAction SilentlyContinue
