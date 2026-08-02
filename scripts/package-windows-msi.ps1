#Requires -Version 7.0
<#
.SYNOPSIS
  Publish win-x64 self-contained app and build an unsigned dual-purpose MSI (WiX v4+).

.DESCRIPTION
  Output under artifacts/windows/:
    ServiceBusExplorer-<version>-win-x64.msi
    *.sha256 sidecar
    MANIFEST.txt (signing.windows.x64=unsigned)

  Requires WiX Toolset SDK (dotnet build of packaging/windows/ServiceBusExplorer.wixproj).
  On non-Windows hosts this script fails with a clear message (build MSI on windows-2022 CI).

.PARAMETER Configuration
  Build configuration (default Release).

.EXAMPLE
  pwsh ./scripts/package-windows-msi.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'lib/PackagingCommon.ps1')
. (Join-Path $PSScriptRoot 'lib/Write-PreviewManifest.ps1')

if (-not $IsWindows) {
    throw 'package-windows-msi.ps1 requires a Windows host with WiX (use windows-2022 GitHub Actions runner).'
}

$repoRoot = Get-SbeRepoRoot -StartPath $PSScriptRoot
$project = Get-SbeAppProjectPath -RepoRoot $repoRoot
$version = Get-SbeAppVersion -ProjectPath $project
$numericVersion = Get-SbeNumericVersion -Version $version
$outRoot = Join-Path $repoRoot 'artifacts/windows'
$publishDir = Join-Path $outRoot 'publish-win-x64'
$wixProj = Join-Path $repoRoot 'packaging/windows/ServiceBusExplorer.wixproj'
$msiName = Get-SbePreviewArtifactBaseName -Version $version -Rid 'win-x64' -Extension 'msi'
$msiPath = Join-Path $outRoot $msiName
$manifestPath = Join-Path $outRoot 'MANIFEST.txt'

if (-not (Test-Path $wixProj)) {
    throw "WiX project missing: $wixProj"
}

if (Test-Path $outRoot) {
    Remove-Item -Recurse -Force $outRoot
}
New-Item -ItemType Directory -Path $outRoot | Out-Null

Write-Host "Publishing win-x64 (self-contained)..."
& dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for win-x64 (exit $LASTEXITCODE)"
}

$exe = Join-Path $publishDir 'ServiceBusExplorer.exe'
if (-not (Test-Path $exe)) {
    throw "Expected published exe missing: $exe"
}

$wixOut = Join-Path $outRoot 'wix-out'
Write-Host "Building MSI with WiX (ProductVersion=$numericVersion)..."
& dotnet build $wixProj `
    -c $Configuration `
    -p:PublishDir="$publishDir\" `
    -p:ProductVersion=$numericVersion `
    -p:OutputPath="$wixOut\" `
    --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    throw "WiX build failed (exit $LASTEXITCODE)"
}

$builtMsi = Get-ChildItem -Path $wixOut -Filter '*.msi' -Recurse | Select-Object -First 1
if (-not $builtMsi) {
    throw "No MSI produced under $wixOut"
}

Copy-Item -Force $builtMsi.FullName $msiPath
$hash = Write-SbeSha256Sidecar -FilePath $msiPath

Set-SbePreviewManifestArtifact `
    -ManifestPath $manifestPath `
    -PlatformKey 'windows.x64' `
    -ArtifactFileName $msiName `
    -Sha256 $hash `
    -Signing 'unsigned' `
    -Notarization 'n/a' `
    -Version $version

# Authoring assertions for smoke tests (UpgradeCode + dual-scope markers).
$wxs = Get-Content -Raw (Join-Path $repoRoot 'packaging/windows/Package.wxs')
if ($wxs -notmatch 'UpgradeCode="A7C4E9F1-2B8D-4E6A-9C31-5D8F0A1B2C3D"') {
    throw 'Package.wxs missing expected UpgradeCode'
}
if ($wxs -notmatch 'Scope="perUserOrMachine"') {
    throw 'Package.wxs must use Scope=perUserOrMachine (ALLUSERS=2 dual-purpose)'
}

Write-Host "MSI_PATH=$msiPath"
Write-Host "MSI_SHA256=$hash"
Write-Host "MANIFEST=$manifestPath"
exit 0
