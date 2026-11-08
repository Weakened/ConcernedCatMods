<# Proves the compiled-API guard catches a changed member in an otherwise
excluded native rendering helper. Builds only; never installs or operates game.
The original source bytes are restored in finally, followed by a clean rebuild. #>
[CmdletBinding()]
param()
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root "src/ConcernedNPC/Body/Dressing/NpcFigureDresser.cs"
$tests = Join-Path $root "src/ConcernedNPC.Tests/ConcernedNPC.Tests.csproj"
$mutex = [Threading.Mutex]::new($false, "Global\ConcernedCatMods-GameBuild")
$held = $false
$original = $null
$changed = $false
$prior = $env:CC_NPC_NATIVE_API
$priorConfiguration = $env:CC_NPC_NATIVE_CONFIGURATION
try {
    try { $held = $mutex.WaitOne([TimeSpan]::FromMinutes(1)) }
    catch [Threading.AbandonedMutexException] { $held = $true }
    if (!$held) { throw "Game-build lock unavailable; no source changed." }
    $original = [IO.File]::ReadAllBytes($target)
    $source = [Text.Encoding]::UTF8.GetString($original)
    $needle = "public void VerifyOnce()"
    if (($source.Split($needle).Length - 1) -ne 1) { throw "Plant anchor changed; refusing to edit." }
    $planted = $source.Replace($needle, "public void VerifyOnce(int codexProbe = 17)")
    $changed = $true
    [IO.File]::WriteAllText($target, $planted, [Text.UTF8Encoding]::new($false))
    & pwsh -NoProfile -File (Join-Path $root "scripts/build.ps1") -Product ConcernedNPC -Configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Plant did not compile; no guard proof." }
    $env:CC_NPC_NATIVE_API = "1"
    $env:CC_NPC_NATIVE_CONFIGURATION = "Release"
    $output = (& dotnet test $tests --no-build -c Release --filter "FullyQualifiedName~NativePublicMemberSurfaceTests" --logger "console;verbosity=normal" 2>&1 | Out-String)
    $testExit = $LASTEXITCODE
    $marker = "VerifyOnce(codexProbe:System.Int32 Optional, HasDefault=System.Int32:17)"
    if ($testExit -eq 0 -or !$output.Contains($marker) -or !$output.Contains("Compiled native API changed")) {
        throw "Expected native-helper API refusal absent; no guard proof. Output: $output"
    }
    Write-Host "[native-api plant] CAUGHT: compiled NpcFigureDresser.VerifyOnce optional parameter/default change."
} finally {
    try {
        if ($changed) {
            [IO.File]::WriteAllBytes($target, $original)
            & pwsh -NoProfile -File (Join-Path $root "scripts/build.ps1") -Product ConcernedNPC -Configuration Release
            if ($LASTEXITCODE -ne 0) { throw "Original source restored, but clean native rebuild failed." }
            & pwsh -NoProfile -File (Join-Path $root "scripts/check-npc-native-api.ps1")
            if ($LASTEXITCODE -ne 0) { throw "Original source restored, but native API baseline failed." }
            Write-Host "[native-api plant] RESTORED: original source bytes, clean native build and baseline pass."
        }
    } finally {
        $env:CC_NPC_NATIVE_API = $prior
        $env:CC_NPC_NATIVE_CONFIGURATION = $priorConfiguration
        if ($held) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}
