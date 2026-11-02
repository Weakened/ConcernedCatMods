<# Metadata-only check of the already-built native library. Run after verify.ps1
under the game-build lock. No assembly is loaded for execution or installed. #>
[CmdletBinding()]
param([ValidateSet("Debug", "Release")][string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$prior = $env:CC_NPC_NATIVE_API
$priorConfiguration = $env:CC_NPC_NATIVE_CONFIGURATION
try {
    $env:CC_NPC_NATIVE_API = "1"
    $env:CC_NPC_NATIVE_CONFIGURATION = $Configuration
    & dotnet test (Join-Path $root "src/ConcernedNPC.Tests/ConcernedNPC.Tests.csproj") --no-build -c $Configuration --filter "FullyQualifiedName~NativePublicMemberSurfaceTests" --logger "console;verbosity=normal"
    if ($LASTEXITCODE -ne 0) { throw "Compiled NPC API check failed (exit $LASTEXITCODE)." }
} finally {
    $env:CC_NPC_NATIVE_API = $prior
    $env:CC_NPC_NATIVE_CONFIGURATION = $priorConfiguration
}
