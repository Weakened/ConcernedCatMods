# Rebuild LadderGait.blend from the C# defaults (DefaultTransitions in TransitionAction.cs, the GaitShape defaults and
# the constants in gait_model.py), then export and regenerate GeneratedGait.cs. Use after tuning a default in C#;
# it OVERWRITES any edit made in the .blend, so do not use it once an animator has changed the file.
$ErrorActionPreference = 'Stop'
$blender = if ($env:BLENDER) { $env:BLENDER } else { 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' }
& $blender --background --factory-startup --python-exit-code 1 --python (Join-Path $PSScriptRoot 'build_gait_blend.py') | Select-String 'saved|wrote|Error'
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
& (Join-Path $PSScriptRoot 'regenerate.ps1')
