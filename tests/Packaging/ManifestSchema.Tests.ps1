#Requires -Version 7.0
<#
.SYNOPSIS
  Manifest schema tests for preview packaging (contracts/artifact-manifest.md).
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $RepoRoot 'scripts/lib/PackagingCommon.ps1')
. (Join-Path $RepoRoot 'scripts/lib/Write-PreviewManifest.ps1')

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "ASSERT FAILED: $Message" }
}

function Assert-False([bool]$Condition, [string]$Message) {
    if ($Condition) { throw "ASSERT FAILED: $Message" }
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

Invoke-Case 'valid full preview manifest passes schema' {
    $m = [ordered]@{
        product                     = 'Service Bus Explorer'
        version                     = '0.0.1-alpha'
        preview                     = 'true'
        'artifact.windows.x64'      = 'ServiceBusExplorer-0.0.1-alpha-win-x64.msi'
        'sha256.windows.x64'        = ('a' * 64)
        'signing.windows.x64'       = 'unsigned'
        'notarization.windows.x64'  = 'n/a'
        'artifact.macos.arm64'      = 'ServiceBusExplorer-0.0.1-alpha-osx-arm64.dmg'
        'sha256.macos.arm64'        = ('b' * 64)
        'signing.macos.arm64'       = 'developer-id'
        'notarization.macos.arm64'  = 'notarized'
        'artifact.linux.x64'        = 'ServiceBusExplorer-0.0.1-alpha-linux-x64.tar.gz'
        'sha256.linux.x64'          = ('c' * 64)
        'signing.linux.x64'         = 'unsigned'
        'notarization.linux.x64'    = 'n/a'
    }
    $r = Test-SbePreviewManifestSchema -Manifest $m -RequiredPlatformKeys @('windows.x64', 'macos.arm64', 'linux.x64')
    Assert-True $r.Ok ("expected ok, got: $($r.Errors -join '; ')")
}

Invoke-Case 'missing product fails' {
    $m = [ordered]@{ version = '0.0.1-alpha'; preview = 'true' }
    $r = Test-SbePreviewManifestSchema -Manifest $m
    Assert-False $r.Ok 'should fail without product'
}

Invoke-Case 'windows signing must be unsigned' {
    $m = [ordered]@{
        product                = 'Service Bus Explorer'
        version                = '0.0.1-alpha'
        preview                = 'true'
        'signing.windows.x64'  = 'authenticode'
    }
    $r = Test-SbePreviewManifestSchema -Manifest $m
    Assert-False $r.Ok 'should reject non-unsigned windows signing'
}

Invoke-Case 'notarized macos requires developer-id signing' {
    $m = [ordered]@{
        product                     = 'Service Bus Explorer'
        version                     = '0.0.1-alpha'
        preview                     = 'true'
        'signing.macos.arm64'       = 'ad-hoc'
        'notarization.macos.arm64'  = 'notarized'
    }
    $r = Test-SbePreviewManifestSchema -Manifest $m
    Assert-False $r.Ok 'should reject notarized without developer-id'
}

Invoke-Case 'round-trip write/read preserves keys' {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ("sbe-manifest-" + [guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $path = Join-Path $dir 'MANIFEST.txt'
        Set-SbePreviewManifestArtifact `
            -ManifestPath $path `
            -PlatformKey 'linux.x64' `
            -ArtifactFileName 'ServiceBusExplorer-0.0.1-alpha-linux-x64.tar.gz' `
            -Sha256 ('d' * 64) `
            -Signing 'unsigned' `
            -Notarization 'n/a' `
            -Version '0.0.1-alpha'
        $read = ConvertFrom-SbePreviewManifest -Path $path
        Assert-True ($read['artifact.linux.x64'] -eq 'ServiceBusExplorer-0.0.1-alpha-linux-x64.tar.gz') 'artifact key'
        Assert-True ($read['sha256.linux.x64'] -eq ('d' * 64)) 'sha key'
        $r = Test-SbePreviewManifestSchema -Manifest $read -RequiredPlatformKeys @('linux.x64')
        Assert-True $r.Ok ("round-trip schema: $($r.Errors -join '; ')")
    }
    finally {
        Remove-Item -Recurse -Force $dir
    }
}

if ($failures -gt 0) {
    Write-Host "ManifestSchema.Tests: $failures failure(s)"
    exit 1
}

Write-Host 'ManifestSchema.Tests: all passed'
exit 0
