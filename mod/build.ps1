# PZA's Camera for Meitou: build the code mod and stage its install folder. Author: PZA. Licence: GPLv3 or later.
#
#   .\build.ps1            build Release, stage out\PZADirectorsKit\ (PZACamera.Meitou.dll, meitou.json, PZADirectorsKit.mod, lang\meitou.*.txt)
#   .\build.ps1 -Check     also run tools\check on the staged folder: Meitou 0.20.1's own code-mod checks, the rules (CamCore), the language
#                          files, the five patches applied to the real MeitouClient.dll, the hotkeys through the real InputBindings, the drawn
#                          camera on a real WorldCamera, and the window built and clicked through Meitou's UiHost, with the text-fit check.
#                          On a copy of the .NET 11 runtime inside the Meitou release; it never starts the game.
#   .\build.ps1 -Fuzz      also run the fuzz harness (tests\fuzz); -Check runs it too (2026-11-18)
#   -MeitouDir <dir>       the unpacked Meitou release (default: MeitouDir in mod\local.props, git-ignored)
#   -RunOn <dir>           run the -Check on ANOTHER Meitou release's runtime and assemblies, with the mod still built against -MeitouDir
#                          (0.20.1, what ships): a 0.20.1 build on 0.21 / 0.22 is how players run it (2026-11-19: Meitou 0.21 changed
#                          UiListItem's constructor and every 0.20.1-built PZA window list threw there; a same-version check never saw it)
#   -PluginDir <dir>       the RE_Kenshi plugin's repo (default ..\..\PZACamera): its dist\PZADirectorsKit.mod is shared, so one folder serves both games (folder PZADirectorsKit since 2026-11-19)
#
# It installs nothing (README "Install"). out\ is git-ignored.
param(
    [switch]$Check,
    [switch]$Fuzz,
    [string]$MeitouDir = "",
    [string]$RunOn = "",
    [string]$PluginDir = "",
    [string]$KenshiDir = "C:\Program Files (x86)\Steam\steamapps\common\Kenshi"
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$localProps = Join-Path $here "local.props"
if ($MeitouDir -eq "" -and (Test-Path $localProps)) { $MeitouDir = ([xml](Get-Content $localProps -Raw)).Project.PropertyGroup.MeitouDir }
if (-not $MeitouDir) { throw "Set -MeitouDir <unpacked Meitou release> or create mod\local.props (see the comment in the csproj)" }
if ($PluginDir -eq "") { $PluginDir = Join-Path $here "..\..\PZACamera" }
$MeitouDir = [System.IO.Path]::GetFullPath($MeitouDir)
$PluginDir = [System.IO.Path]::GetFullPath($PluginDir)
if (-not (Test-Path (Join-Path $MeitouDir "MeitouClient.dll"))) { throw "MeitouClient.dll not found in $MeitouDir" }
if (-not (Test-Path (Join-Path $PluginDir "dist\PZADirectorsKit.mod"))) { throw "the plugin's dist\PZADirectorsKit.mod not found in $PluginDir" }

& dotnet build (Join-Path $here "PZACamera.Meitou.csproj") -c Release -nologo "-p:MeitouDir=$MeitouDir"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

$bin = Get-ChildItem (Join-Path $here "bin\Release") -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$stage = Join-Path $here "out\PZADirectorsKit"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force (Join-Path $stage "lang") | Out-Null
Copy-Item (Join-Path $bin.FullName "PZACamera.Meitou.dll") $stage -Force
Copy-Item (Join-Path $here "meitou.json") $stage -Force
# shared with the RE_Kenshi plugin: the data mod Meitou lists the folder by
Copy-Item (Join-Path $PluginDir "dist\PZADirectorsKit.mod") $stage -Force
Copy-Item (Join-Path $here "lang\meitou.*.txt") (Join-Path $stage "lang") -Force

Write-Host "staged $stage :"
Get-ChildItem $stage -Recurse -File | ForEach-Object { Write-Host ("  {0,-28} {1,8} bytes" -f $_.FullName.Substring($stage.Length + 1), $_.Length) }

if ($Check) {
    $runDir = if ($RunOn -ne "") { [System.IO.Path]::GetFullPath($RunOn) } else { $MeitouDir }
    if (-not (Test-Path (Join-Path $runDir "MeitouClient.dll"))) { throw "MeitouClient.dll not found in $runDir" }
    Write-Host "check: the mod built against $MeitouDir, run on $runDir"
    $rt = Join-Path $here "out\checkrt"
    if (Test-Path $rt) { Remove-Item -Recurse -Force $rt }
    New-Item -ItemType Directory -Force $rt | Out-Null
    $patterns = "System*.dll", "Microsoft.*.dll", "mscorlib.dll", "netstandard.dll", "WindowsBase.dll", "coreclr.dll", "clrjit.dll",
                "clrgc.dll", "hostfxr.dll", "hostpolicy.dll", "Meitou*.dll", "0Harmony.dll", "Bepu*.dll", "DotRecast*.dll",
                "Silk.NET*.dll", "MeitouClient.deps.json"   # GameLoop's fields use Silk.NET: without it the type does not load (GetType = null)
    foreach ($p in $patterns) { Copy-Item (Join-Path $runDir $p) $rt -Force }
    $stagedDll = Join-Path $stage "PZACamera.Meitou.dll"
    & dotnet build (Join-Path $here "tools\check\check.csproj") -c Release -nologo "-p:MeitouDir=$runDir" "-p:ModDll=$stagedDll" -o (Join-Path $here "out\checkbin")
    if ($LASTEXITCODE -ne 0) { throw "check tool build failed ($LASTEXITCODE)" }
    Copy-Item (Join-Path $here "out\checkbin\check.dll") $rt -Force
    Copy-Item $stagedDll $rt -Force
    $cfg = '{ "runtimeOptions": { "tfm": "net11.0", "includedFrameworks": [ { "name": "Microsoft.NETCore.App", "version": "11.0.0" } ] } }'
    Set-Content -Path (Join-Path $rt "check.runtimeconfig.json") -Value $cfg -Encoding ascii
    Push-Location $rt
    try { & dotnet exec --runtimeconfig check.runtimeconfig.json check.dll $stage $KenshiDir } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "check FAILED ($LASTEXITCODE)" }
}

# 2026-11-18: the fuzz harness (tests\fuzz, PZA's Meitou UI fuzz kit) on the same Meitou release: -Fuzz runs it, and so does every -Check
# (a release also needs the mutation check, PZAMeitouUi\fuzz\mutate.py). It never starts the game.
if ($Fuzz -or $Check) {
    $fuzzRunner = Join-Path $here "..\..\PZAMeitouUi\fuzz\run_fuzz.ps1"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $fuzzRunner -Repo (Join-Path $here "..") -MeitouDir $MeitouDir
    if ($LASTEXITCODE -ne 0) { throw "fuzz FAILED ($LASTEXITCODE): report in tests\out\fuzz\fuzz-report.txt" }
}
