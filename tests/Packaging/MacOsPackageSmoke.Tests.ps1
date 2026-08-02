#Requires -Version 7.0
<#
.SYNOPSIS
  macOS packaging assertions: arm64-only RID + sign-before-notarize order.
  Live notarize is skipped unless SBE_NOTARIZE=1.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path

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

$scriptPath = Join-Path $RepoRoot 'scripts/package-macos-internal.sh'
$fastfile = Join-Path $RepoRoot 'fastlane/Fastfile'

Invoke-Case 'package-macos-internal.sh defaults to osx-arm64 only' {
    Assert-True (Test-Path $scriptPath) 'script missing'
    $sh = Get-Content -Raw $scriptPath
    Assert-True ($sh -match 'RID="\$\{RID:-osx-arm64\}"') 'default RID osx-arm64'
    Assert-True ($sh -match 'osx-x64 is deferred') 'rejects/defer x64 messaging'
    Assert-True ($sh -notmatch 'osx-x64\) EXPECTED_ARCH') 'no osx-x64 accept branch'
}

Invoke-Case 'sign before DMG before notarize order documented in script' {
    $sh = Get-Content -Raw $scriptPath
    Assert-True ($sh -match 'Sign → DMG first') 'sign→DMG comment'
    $signIdx = $sh.IndexOf('sign_bundle')
    $dmgIdx = $sh.IndexOf('hdiutil create')
    $notaIdx = $sh.IndexOf('fastlane macos_notarize_dmg')
    Assert-True ($signIdx -ge 0 -and $dmgIdx -gt $signIdx) 'sign before dmg'
    Assert-True ($notaIdx -gt $dmgIdx) 'fastlane notarize after dmg'
}

Invoke-Case 'Fastfile macos_notarize_dmg lane present' {
    Assert-True (Test-Path $fastfile) 'Fastfile missing'
    $ff = Get-Content -Raw $fastfile
    Assert-True ($ff -match 'lane :macos_notarize_dmg') 'lane'
    Assert-True ($ff -match 'notarize\(') 'notarize action'
    Assert-True ($ff -match 'com\.servicebusexplorer\.internal') 'bundle id'
}

Invoke-Case 'MANIFEST contract keys when artifacts present' {
    $manifestPath = Join-Path $RepoRoot 'artifacts/macos-internal/MANIFEST.txt'
    if (-not (Test-Path $manifestPath)) {
        Write-Host 'SKIP: no macos MANIFEST yet'
        return
    }
    . (Join-Path $RepoRoot 'scripts/lib/Write-PreviewManifest.ps1')
    $m = ConvertFrom-SbePreviewManifest -Path $manifestPath
    Assert-True ($m.Contains('artifact.macos.arm64')) 'artifact key'
    Assert-True ($m.Contains('signing.macos.arm64')) 'signing key'
    Assert-True ($m.Contains('notarization.macos.arm64')) 'notarization key'
}

if ($env:SBE_NOTARIZE -eq '1') {
    Invoke-Case 'live notarize gate (SBE_NOTARIZE=1)' {
        Assert-True (-not [string]::IsNullOrWhiteSpace($env:APP_STORE_CONNECT_API_KEY_PATH)) 'API key path'
        Assert-True (Test-Path $env:APP_STORE_CONNECT_API_KEY_PATH) 'API key file'
        Write-Host 'Live notarize credentials present; run package-macos-internal.sh with NOTARIZE=1 separately.'
    }
}
else {
    Write-Host 'SKIP: live notarize (set SBE_NOTARIZE=1 to enable)'
}

if ($failures -gt 0) {
    Write-Host "MacOsPackageSmoke.Tests: $failures failure(s)"
    exit 1
}

Write-Host 'MacOsPackageSmoke.Tests: all passed'
exit 0
