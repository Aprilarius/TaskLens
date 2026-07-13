param([switch]$Clean)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root 'dist'
if ($Clean -and (Test-Path -LiteralPath $out)) { Remove-Item -LiteralPath $out -Recurse -Force }
& (Join-Path $root 'tools\Test-Localization.ps1')
dotnet publish (Join-Path $root 'TaskLens.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false -o $out
Get-ChildItem -LiteralPath $out | Where-Object Name -ne 'TaskLens.exe' | Remove-Item -Force -Recurse
$exe = Join-Path $out 'TaskLens.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'TaskLens.exe was not produced.' }
Write-Host "Ready: $exe"
