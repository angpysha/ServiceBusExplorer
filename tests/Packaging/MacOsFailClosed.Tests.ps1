#Requires -Version 7.0
<#
.SYNOPSIS
  Fail-closed CI assertions: notarize mode must not upload macOS artifacts on failure.
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

$workflow = Join-Path $RepoRoot '.github/workflows/preview-packages.yml'
$pkgScript = Join-Path $RepoRoot 'scripts/package-macos-internal.sh'
$importScript = Join-Path $RepoRoot 'scripts/ci/import-apple-signing.sh'
$secretsContract = Join-Path $RepoRoot 'specs/002-preview-installer-packaging/contracts/github-secrets.md'

Invoke-Case 'package script fails closed when NOTARIZE=1 without Developer ID' {
    $sh = Get-Content -Raw $pkgScript
    Assert-True ($sh -match 'fail-closed') 'mentions fail-closed'
    Assert-True ($sh -match 'NOTARIZE=1 requires a Developer ID') 'requires Developer ID'
    Assert-True ($sh -match 'APP_STORE_CONNECT_API_KEY_PATH') 'requires ASC API key path'
}

Invoke-Case 'import script prepares ASC API key and can require it' {
    $sh = Get-Content -Raw $importScript
    Assert-True ($sh -match 'APP_STORE_CONNECT_API_KEY_P8_BASE64') 'p8 secret'
    Assert-True ($sh -match 'api_key\.json') 'writes api_key.json'
    Assert-True ($sh -match 'No Mac App Store provisioning profile') 'no provisioning profile'
}

Invoke-Case 'required secret names present in contract' {
    $doc = Get-Content -Raw $secretsContract
    foreach ($name in @(
            'MACOS_CERTIFICATE_BASE64',
            'MACOS_CERTIFICATE_PWD',
            'KEYCHAIN_PASSWORD',
            'APP_STORE_CONNECT_API_KEY_ID',
            'APP_STORE_CONNECT_ISSUER_ID',
            'APP_STORE_CONNECT_API_KEY_P8_BASE64'
        )) {
        Assert-True ($doc -match [regex]::Escape($name)) "secret $name"
    }
}

Invoke-Case 'workflow fail-closed: secrets gate before package; DMG upload only (no zip)' {
    Assert-True (Test-Path $workflow) 'workflow missing'
    $yml = Get-Content -Raw $workflow
    Assert-True ($yml -match 'package-macos-internal\.sh') 'macos package script'
    Assert-True ($yml -match 'import-apple-signing\.sh') 'import signing script'
    Assert-True ($yml -match 'Require Apple signing \+ ASC API key secrets') 'secrets gate step'
    Assert-True ($yml -match 'fail-closed') 'fail-closed'
    Assert-True ($yml -match 'artifacts/macos-internal/\*\.dmg') 'dmg upload'
    Assert-True ($yml -notmatch 'artifacts/macos-internal/\*\.zip') 'zip demoted from upload'
}

if ($failures -gt 0) {
    Write-Host "MacOsFailClosed.Tests: $failures failure(s)"
    exit 1
}

Write-Host 'MacOsFailClosed.Tests: all passed'
exit 0
