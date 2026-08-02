#Requires -Version 7.0
<#
.SYNOPSIS
  Windows MSI smoke assertions (WiX dual-scope / unsigned / product identity).
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

$wxsPath = Join-Path $RepoRoot 'packaging/windows/Package.wxs'
$wixProj = Join-Path $RepoRoot 'packaging/windows/ServiceBusExplorer.wixproj'

Invoke-Case 'WiX Package.wxs exists with UpgradeCode' {
    Assert-True (Test-Path $wxsPath) 'Package.wxs missing'
    $wxs = Get-Content -Raw $wxsPath
    Assert-True ($wxs -match 'UpgradeCode="A7C4E9F1-2B8D-4E6A-9C31-5D8F0A1B2C3D"') 'UpgradeCode GUID'
}

Invoke-Case 'dual-purpose Scope=perUserOrMachine (ALLUSERS=2 / per-user default)' {
    $wxs = Get-Content -Raw $wxsPath
    Assert-True ($wxs -match 'Scope="perUserOrMachine"') 'Scope perUserOrMachine'
    Assert-True ($wxs -match 'WixUI_Advanced') 'WixUI_Advanced for scope choice'
}

Invoke-Case 'WiX project targets WiX SDK 4+' {
    Assert-True (Test-Path $wixProj) 'wixproj missing'
    $proj = Get-Content -Raw $wixProj
    Assert-True ($proj -match 'WixToolset\.Sdk/') 'WixToolset.Sdk reference'
}

Invoke-Case 'package-windows-msi.ps1 orchestrator exists' {
    Assert-True (Test-Path (Join-Path $RepoRoot 'scripts/package-windows-msi.ps1')) 'script missing'
}

$windowsDir = Join-Path $RepoRoot 'artifacts/windows'
if (Test-Path (Join-Path $windowsDir 'MANIFEST.txt')) {
    Invoke-Case 'built MSI MANIFEST is unsigned windows.x64' {
        $m = ConvertFrom-SbePreviewManifest -Path (Join-Path $windowsDir 'MANIFEST.txt')
        Assert-True ([string]$m['signing.windows.x64'] -eq 'unsigned') 'signing.windows.x64'
        Assert-True ([string]$m['notarization.windows.x64'] -eq 'n/a') 'notarization.windows.x64'
        $msi = Join-Path $windowsDir ([string]$m['artifact.windows.x64'])
        Assert-True (Test-Path $msi) 'msi file'
        Assert-True (([IO.Path]::GetExtension($msi)) -eq '.msi') 'extension'
        Assert-True (Test-SbeSha256Sidecar -FilePath $msi) 'sha256'
    }
}
else {
    Write-Host 'SKIP: built MSI artifacts (run package-windows-msi.ps1 on Windows+WiX)'
}

if ($failures -gt 0) {
    Write-Host "WindowsMsiSmoke.Tests: $failures failure(s)"
    exit 1
}

Write-Host 'WindowsMsiSmoke.Tests: all passed'
exit 0
