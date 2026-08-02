#Requires -Version 7.0
<#
.SYNOPSIS
  Packaging smoke entrypoint: checksum sidecars + required MANIFEST keys.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $RepoRoot 'scripts/lib/PackagingCommon.ps1')
. (Join-Path $RepoRoot 'scripts/lib/Write-PreviewManifest.ps1')

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "ASSERT FAILED: $Message" }
}

$failures = 0
function Invoke-Case([string]$Name, [scriptblock]$Body) {
    try {
        & $Body
        Write-Host "PASS: $Name"
    }
    catch {
        $script:failures++
        Write-Host "FAIL: $Name — $($_.Exception.Message)"
    }
}

function Test-ArtifactDirSmoke {
    param(
        [Parameter(Mandatory)][string]$ArtifactDir,
        [Parameter(Mandatory)][string]$PlatformKey,
        [Parameter(Mandatory)][string]$ExtensionPattern
    )

    Assert-True (Test-Path $ArtifactDir) "artifact dir missing: $ArtifactDir"
    $manifestPath = Join-Path $ArtifactDir 'MANIFEST.txt'
    Assert-True (Test-Path $manifestPath) "MANIFEST.txt missing under $ArtifactDir"

    $manifest = ConvertFrom-SbePreviewManifest -Path $manifestPath
    $schema = Test-SbePreviewManifestSchema -Manifest $manifest -RequiredPlatformKeys @($PlatformKey)
    Assert-True $schema.Ok ("schema: $($schema.Errors -join '; ')")

    $artifactName = [string]$manifest["artifact.$PlatformKey"]
    Assert-True ($artifactName -match $ExtensionPattern) "artifact name '$artifactName' should match $ExtensionPattern"
    $artifactPath = Join-Path $ArtifactDir $artifactName
    Assert-True (Test-Path $artifactPath) "artifact file missing: $artifactPath"
    Assert-True (Test-SbeSha256Sidecar -FilePath $artifactPath) "sha256 sidecar invalid for $artifactName"
    Assert-True ([string]$manifest["sha256.$PlatformKey"] -eq (Get-FileHash -Algorithm SHA256 -Path $artifactPath).Hash.ToLowerInvariant()) `
        'MANIFEST sha256 must match file hash'
}

Invoke-Case 'helpers produce valid sha256 sidecar format' {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ("sbe-smoke-" + [guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $file = Join-Path $dir 'sample.bin'
        Set-Content -Path $file -Value 'smoke' -NoNewline
        $hash = Write-SbeSha256Sidecar -FilePath $file
        Assert-True ($hash.Length -eq 64) 'hash length'
        Assert-True (Test-SbeSha256Sidecar -FilePath $file) 'sidecar verify'
    }
    finally {
        Remove-Item -Recurse -Force $dir
    }
}

Invoke-Case 'platform key mapping for supported RIDs' {
    Assert-True ((ConvertTo-SbeManifestPlatformKey 'win-x64') -eq 'windows.x64') 'win-x64'
    Assert-True ((ConvertTo-SbeManifestPlatformKey 'osx-arm64') -eq 'macos.arm64') 'osx-arm64'
    Assert-True ((ConvertTo-SbeManifestPlatformKey 'linux-x64') -eq 'linux.x64') 'linux-x64'
}

# Optional live smoke against produced artifacts (skip if absent).
$linuxDir = Join-Path $RepoRoot 'artifacts/preview'
$windowsDir = Join-Path $RepoRoot 'artifacts/windows'
$macosDir = Join-Path $RepoRoot 'artifacts/macos-internal'

if ((Test-Path (Join-Path $linuxDir 'MANIFEST.txt'))) {
    Invoke-Case 'linux preview artifact smoke' {
        Test-ArtifactDirSmoke -ArtifactDir $linuxDir -PlatformKey 'linux.x64' -ExtensionPattern '\.tar\.gz$'
    }
}
else {
    Write-Host 'SKIP: linux artifacts/preview (run publish-preview.ps1 -Rids linux-x64)'
}

if ((Test-Path (Join-Path $windowsDir 'MANIFEST.txt'))) {
    Invoke-Case 'windows MSI artifact smoke' {
        Test-ArtifactDirSmoke -ArtifactDir $windowsDir -PlatformKey 'windows.x64' -ExtensionPattern '\.msi$'
        $m = ConvertFrom-SbePreviewManifest -Path (Join-Path $windowsDir 'MANIFEST.txt')
        Assert-True ([string]$m['signing.windows.x64'] -eq 'unsigned') 'windows must be unsigned'
    }
}
else {
    Write-Host 'SKIP: windows artifacts/windows (run package-windows-msi.ps1 on Windows+WiX)'
}

if ((Test-Path (Join-Path $macosDir 'MANIFEST.txt'))) {
    Invoke-Case 'macos DMG artifact smoke' {
        Test-ArtifactDirSmoke -ArtifactDir $macosDir -PlatformKey 'macos.arm64' -ExtensionPattern '\.dmg$'
    }
}
else {
    Write-Host 'SKIP: macos artifacts/macos-internal (run package-macos-internal.sh on Apple Silicon)'
}

if ($failures -gt 0) {
    Write-Host "PackageSmoke.Tests: $failures failure(s)"
    exit 1
}

Write-Host 'PackageSmoke.Tests: all passed'
exit 0
