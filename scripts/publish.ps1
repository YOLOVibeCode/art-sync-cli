#Requires -Version 5.1
<#
.SYNOPSIS
    Publish ArtSync as three Devart-compatible exe names.

.DESCRIPTION
    Builds ArtSync.Cli and copies the output exe to three side-by-side names:
      schemacompare.exe   (drop-in for schemacompare.com)
      datacompare.exe     (drop-in for datacompare.com)
      dbforgesql.exe      (drop-in for dbforgesql.com)

    Run this script from the repository root after installing the .NET 10 SDK.

.PARAMETER Runtime
    RID to publish for. Defaults to win-x64.
    Use win-arm64 for ARM Windows, or linux-x64 for Linux.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER OutputRoot
    Parent folder for publish output. Defaults to publish/ in the repo root.

.PARAMETER SelfContained
    Whether to produce a self-contained exe (no .NET runtime required on target).
    Defaults to $true.

.EXAMPLE
    .\scripts\publish.ps1
    .\scripts\publish.ps1 -Runtime win-x64 -OutputRoot C:\Deploy\ArtSync
    .\scripts\publish.ps1 -SelfContained $false   # framework-dependent, smaller output
#>
param(
    [string] $Runtime       = 'win-x64',
    [string] $Configuration = 'Release',
    [string] $OutputRoot    = (Join-Path $PSScriptRoot '..\publish'),
    [bool]   $SelfContained = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$CliProject = Join-Path $RepoRoot 'src\ArtSync.Cli\ArtSync.Cli.csproj'
$OutDir     = Join-Path $OutputRoot "artsync-$Runtime"

Write-Host "Publishing ArtSync.Cli → $OutDir" -ForegroundColor Cyan

$dotnetArgs = @(
    'publish', $CliProject,
    '-r', $Runtime,
    '-c', $Configuration,
    '-o', $OutDir,
    '--self-contained', $SelfContained.ToString().ToLower(),
    '/p:PublishSingleFile=true',
    '/p:IncludeNativeLibrariesForSelfExtract=true'
)
& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Locate the built exe
$srcExe = Get-ChildItem -Path $OutDir -Filter 'ArtSync.Cli.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $srcExe) {
    $srcExe = Get-ChildItem -Path $OutDir -Filter 'ArtSync.Cli'   -ErrorAction SilentlyContinue | Select-Object -First 1
}
if (-not $srcExe) { throw "Cannot find ArtSync.Cli[.exe] in $OutDir" }

$names = @('schemacompare', 'datacompare', 'dbforgesql')
foreach ($name in $names) {
    $destName = if ($srcExe.Extension) { "$name$($srcExe.Extension)" } else { $name }
    $dest = Join-Path $OutDir $destName
    Copy-Item -Path $srcExe.FullName -Destination $dest -Force
    Write-Host "  Wrote $destName" -ForegroundColor Green
}

Write-Host ""
Write-Host "Published to: $OutDir" -ForegroundColor Cyan
Write-Host "Exes:" -ForegroundColor Cyan
Get-ChildItem $OutDir -Filter '*.exe' | Select-Object -ExpandProperty Name | ForEach-Object {
    Write-Host "  $_"
}
