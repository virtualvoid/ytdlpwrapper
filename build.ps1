<#
.SYNOPSIS
  Otestuje, publikuje (self-contained win-x64) a zbalí aplikáciu do inštalátora.
.EXAMPLE
  .\build.ps1                 # test + publish + inštalátor
  .\build.ps1 -SkipTests
#>
param(
    [string]$Version = '1.0.0',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publishDir = Join-Path $root 'artifacts\publish'
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) sa nenašiel.' }

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'tests\YtMp3Downloader.Tests') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Testy zlyhali.' }
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish (Join-Path $root 'src\YtMp3Downloader') -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishReadyToRun=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'Publish zlyhal.' }

& $iscc "/DMyAppVersion=$Version" "/DPublishDir=$publishDir" (Join-Path $root 'installer\YtMp3Downloader.iss')
if ($LASTEXITCODE -ne 0) { throw 'Kompilácia inštalátora zlyhala.' }

Get-ChildItem (Join-Path $root 'artifacts\installer') -Filter *.exe |
    ForEach-Object { "{0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB) }
