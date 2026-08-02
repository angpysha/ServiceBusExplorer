#Requires -Version 7.0
<#
.SYNOPSIS
  Publish self-contained Avalonia preview archives (linux-x64 primary; optional zip RIDs).

.DESCRIPTION
  Builds RID artifacts under artifacts/preview/ using shared PackagingCommon + Write-PreviewManifest.
  Evaluator-primary installers are MSI (Windows) and notarized DMG (macOS) — produced by
  package-windows-msi.ps1 and package-macos-internal.sh respectively. This script emits
  linux-x64.tar.gz as the Linux preview archive and may optionally emit demoted zip archives
  for non-installer RIDs (not the macOS Gatekeeper-friendly path).

.PARAMETER Configuration
  Build configuration (default Release).

.PARAMETER Rids
  Comma-separated RID list. Default: linux-x64

.PARAMETER SkipOsx
  Skip osx-* RIDs (useful on Windows agents).

.EXAMPLE
  pwsh ./scripts/publish-preview.ps1

.EXAMPLE
  pwsh ./scripts/publish-preview.ps1 -Rids linux-x64
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Rids = 'linux-x64',
    [switch] $SkipOsx
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'lib/PackagingCommon.ps1')
. (Join-Path $PSScriptRoot 'lib/Write-PreviewManifest.ps1')

$repoRoot = Get-SbeRepoRoot -StartPath $PSScriptRoot
$project = Get-SbeAppProjectPath -RepoRoot $repoRoot
$outRoot = Join-Path $repoRoot 'artifacts/preview'
$version = Get-SbeAppVersion -ProjectPath $project
$manifestPath = Join-Path $outRoot 'MANIFEST.txt'

$ridList = @($Rids.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($SkipOsx) {
    $ridList = @($ridList | Where-Object { $_ -notlike 'osx-*' })
}

if ($ridList.Count -eq 0) {
    throw 'No RIDs specified after filtering.'
}

if (Test-Path $outRoot) {
    Remove-Item -Recurse -Force $outRoot
}
New-Item -ItemType Directory -Path $outRoot | Out-Null

Write-SbePreviewManifest -Path $manifestPath -Entries ([ordered]@{
        product         = 'Service Bus Explorer'
        version         = $version
        preview         = 'true'
        configuration   = $Configuration
        note            = 'Primary Windows/macOS evaluators use MSI/DMG scripts; this folder is linux archive (+ optional demoted zips).'
    })

foreach ($rid in $ridList) {
    if ($rid -like 'osx-*') {
        Write-Warning "osx RID '$rid' zip from publish-preview.ps1 is demoted; prefer ./scripts/package-macos-internal.sh for notarized DMG (osx-arm64 only)."
    }
    if ($rid -eq 'win-x64') {
        Write-Warning "win-x64 zip from publish-preview.ps1 is demoted; prefer ./scripts/package-windows-msi.ps1 for the evaluator MSI."
    }

    $publishDir = Join-Path $outRoot "publish-$rid"
    Write-Host "Publishing $rid..."
    & dotnet publish $project `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -o $publishDir `
        -p:PublishSingleFile=false `
        -p:DebugType=None `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $rid (exit $LASTEXITCODE)"
    }

    $extension = if ($rid -eq 'linux-x64') { 'tar.gz' } else { 'zip' }
    $artifactName = Get-SbePreviewArtifactBaseName -Version $version -Rid $rid -Extension $extension
    $artifactPath = Join-Path $outRoot $artifactName

    if ($rid -eq 'linux-x64') {
        if (-not (Get-Command tar -ErrorAction SilentlyContinue)) {
            throw 'tar is required to create linux-x64.tar.gz'
        }
        Push-Location $publishDir
        try {
            & tar -czf $artifactPath .
            if ($LASTEXITCODE -ne 0) { throw "tar failed for $rid" }
        }
        finally {
            Pop-Location
        }
    }
    else {
        Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $artifactPath -Force
    }

    $hash = Write-SbeSha256Sidecar -FilePath $artifactPath
    $platformKey = ConvertTo-SbeManifestPlatformKey -Rid $rid
    Set-SbePreviewManifestArtifact `
        -ManifestPath $manifestPath `
        -PlatformKey $platformKey `
        -ArtifactFileName $artifactName `
        -Sha256 $hash `
        -Signing 'unsigned' `
        -Notarization 'n/a' `
        -Version $version

    Write-Host "Wrote $artifactPath ($hash)"
}

Write-Host "Preview packages ready under $outRoot"
exit 0
