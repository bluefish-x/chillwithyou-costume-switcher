# Build the CostumeSwitcher plugin with the Roslyn csc that ships with VS2022
# (no .NET SDK needed on this machine)
param(
  [string]$GameDir = 'C:\SteamLibrary\steamapps\common\Chill with You Lo-Fi Story'
)

$ErrorActionPreference = 'Stop'

$csc     = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$managed = Join-Path $GameDir 'Chill With You_Data\Managed'
$core    = Join-Path $GameDir 'BepInEx\core'
$src     = Join-Path $PSScriptRoot '..\src\CostumeSwitcher\CostumeSwitcherPlugin.cs'
$outDir  = Join-Path $GameDir 'BepInEx\plugins\CostumeSwitcher'

if (-not (Test-Path $csc))     { throw "csc not found: $csc" }
if (-not (Test-Path $managed)) { throw "Managed folder not found: $managed" }
if (-not (Test-Path $core))    { throw "BepInEx not installed in: $GameDir" }

$refs = @(
  "$managed\mscorlib.dll"
  "$managed\netstandard.dll"
  "$managed\System.dll"
  "$managed\System.Core.dll"
  "$managed\UnityEngine.dll"
  "$managed\UnityEngine.UI.dll"
  "$managed\UnityEngine.UIModule.dll"
  "$managed\UnityEngine.CoreModule.dll"
  "$managed\UnityEngine.IMGUIModule.dll"
  "$managed\UnityEngine.InputLegacyModule.dll"
  "$managed\UnityEngine.TextRenderingModule.dll"
  "$managed\UniTask.dll"
  "$managed\Assembly-CSharp.dll"
  "$core\BepInEx.dll"
  "$core\0Harmony.dll"
)

foreach ($r in $refs) { if (-not (Test-Path $r)) { throw "missing reference: $r" } }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$dll = Join-Path $outDir 'CostumeSwitcher.dll'

$cscArgs = @(
  '/nologo', '/target:library', '/nostdlib+', '/langversion:9.0',
  '/codepage:65001', "/out:$dll",
  '/warn:0', '/optimize+'
) + ($refs | ForEach-Object { "/r:$_" }) + @($src)

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "compile failed ($LASTEXITCODE)" }

Get-Item $dll | Select-Object FullName, Length, LastWriteTime | Format-List

