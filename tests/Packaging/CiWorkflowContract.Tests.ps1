#Requires -Version 7.0
<#
.SYNOPSIS
  CI workflow contract checks for preview-packages.yml (fail-closed + required secrets).
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path

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

$workflow = Join-Path $RepoRoot '.github/workflows/preview-packages.yml'
$secretsContract = Join-Path $RepoRoot 'specs/002-preview-installer-packaging/contracts/github-secrets.md'
$RequiredSecrets = @(
    'MACOS_CERTIFICATE_BASE64',
    'MACOS_CERTIFICATE_PWD',
    'KEYCHAIN_PASSWORD',
    'APP_STORE_CONNECT_API_KEY_ID',
    'APP_STORE_CONNECT_ISSUER_ID',
    'APP_STORE_CONNECT_API_KEY_P8_BASE64'
)

Invoke-Case 'workflow file exists' {
    Assert-True (Test-Path $workflow) 'preview-packages.yml missing'
}

$yml = Get-Content -Raw $workflow

Invoke-Case 'Windows job uses package-windows-msi.ps1 on windows-2022' {
    Assert-True ($yml -match 'windows-2022') 'windows-2022 runner'
    Assert-True ($yml -match 'package-windows-msi\.ps1') 'MSI script'
    Assert-True ($yml -match 'artifacts/windows/\*\.msi') 'MSI upload path'
}

Invoke-Case 'Linux job uses publish-preview.ps1 linux-x64 on ubuntu' {
    Assert-True ($yml -match 'ubuntu-22\.04') 'ubuntu runner'
    Assert-True ($yml -match 'publish-preview\.ps1 -Rids linux-x64') 'linux publish'
    Assert-True ($yml -match 'linux-x64\.tar\.gz') 'tar.gz upload'
}

Invoke-Case 'macOS job is macos-14 osx-arm64 only (no macos-13 / osx-x64)' {
    Assert-True ($yml -match 'macos-14') 'macos-14'
    Assert-True ($yml -match 'RID: osx-arm64') 'osx-arm64'
    Assert-False ($yml -match 'macos-13') 'must not use macos-13'
    Assert-False ($yml -match 'osx-x64') 'must not target osx-x64'
}

Invoke-Case 'macOS wires import-apple-signing + package-macos-internal + NOTARIZE=1' {
    Assert-True ($yml -match 'import-apple-signing\.sh') 'import script'
    Assert-True ($yml -match 'package-macos-internal\.sh') 'package script'
    Assert-True ($yml -match 'NOTARIZE: "1"') 'notarize enabled'
}

Invoke-Case 'fail-closed: require all contract secrets before package; upload after success' {
    foreach ($name in $RequiredSecrets) {
        Assert-True ($yml -match [regex]::Escape($name)) "workflow references $name"
    }
    Assert-True ($yml -match 'fail-closed') 'fail-closed messaging'
    Assert-True ($yml -match 'Require Apple signing \+ ASC API key secrets') 'require-secrets step'
    # Upload step must list DMG only (no zip primary)
    Assert-True ($yml -match 'artifacts/macos-internal/\*\.dmg') 'dmg upload'
    Assert-False ($yml -match 'artifacts/macos-internal/\*\.zip') 'no zip in macOS upload list'
}

Invoke-Case 'contract github-secrets.md lists the same required secret names' {
    $doc = Get-Content -Raw $secretsContract
    foreach ($name in $RequiredSecrets) {
        Assert-True ($doc -match [regex]::Escape($name)) "contract $name"
    }
}

Invoke-Case 'optional draft release job present' {
    Assert-True ($yml -match 'draft-release:') 'draft-release job'
    Assert-True ($yml -match 'create_release') 'create_release input'
}

Invoke-Case 'environment macos-notarize recommended' {
    Assert-True ($yml -match 'environment:\s*macos-notarize') 'Environment macos-notarize'
}

if ($failures -gt 0) {
    Write-Host "CiWorkflowContract.Tests: $failures failure(s)"
    exit 1
}

Write-Host 'CiWorkflowContract.Tests: all passed'
exit 0
