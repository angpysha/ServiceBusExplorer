#Requires -Version 7.0
<#
.SYNOPSIS
  Shared helpers for preview packaging scripts (version, RID, checksums, paths).
#>

Set-StrictMode -Version Latest

function Get-SbeRepoRoot {
    [CmdletBinding()]
    param(
        [string] $StartPath = $PSScriptRoot
    )

    $dir = $StartPath
    while ($dir) {
        if (Test-Path (Join-Path $dir 'src/App/App.csproj')) {
            return (Resolve-Path $dir).Path
        }
        $parent = Split-Path $dir -Parent
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    throw 'Could not locate repository root (expected src/App/App.csproj).'
}

function Get-SbeAppProjectPath {
    [CmdletBinding()]
    param(
        [string] $RepoRoot = (Get-SbeRepoRoot)
    )
    return (Join-Path $RepoRoot 'src/App/App.csproj')
}

function Get-SbeAppVersion {
    [CmdletBinding()]
    param(
        [string] $ProjectPath = (Get-SbeAppProjectPath)
    )

    if (-not (Test-Path $ProjectPath)) {
        throw "App project not found: $ProjectPath"
    }

    $xml = [xml](Get-Content -Raw $ProjectPath)
    $node = $xml.Project.PropertyGroup.Version | Select-Object -First 1
    if (-not $node) {
        throw "No <Version> in $ProjectPath"
    }
    return [string]$node
}

function Get-SbeNumericVersion {
    [CmdletBinding()]
    param(
        [string] $Version = (Get-SbeAppVersion)
    )
    return ($Version -split '-', 2)[0]
}

function Write-SbeSha256Sidecar {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $FilePath
    )

    if (-not (Test-Path $FilePath)) {
        throw "Cannot hash missing file: $FilePath"
    }

    $hash = (Get-FileHash -Algorithm SHA256 -Path $FilePath).Hash.ToLowerInvariant()
    $name = Split-Path $FilePath -Leaf
    $sidecar = "$FilePath.sha256"
    Set-Content -Path $sidecar -Value "$hash  $name" -NoNewline -Encoding utf8
    return $hash
}

function Test-SbeSha256Sidecar {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $FilePath
    )

    $sidecar = "$FilePath.sha256"
    if (-not (Test-Path $FilePath)) { return $false }
    if (-not (Test-Path $sidecar)) { return $false }

    $expected = (Get-FileHash -Algorithm SHA256 -Path $FilePath).Hash.ToLowerInvariant()
    $line = (Get-Content -Raw $sidecar).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(\S+)$') {
        return $false
    }
    $actualHash = $Matches[1].ToLowerInvariant()
    $actualName = $Matches[2]
    $leaf = Split-Path $FilePath -Leaf
    return ($actualHash -eq $expected) -and ($actualName -eq $leaf)
}

function ConvertTo-SbeManifestPlatformKey {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Rid
    )

    switch -Regex ($Rid) {
        '^win-x64$' { return 'windows.x64' }
        '^win-arm64$' { return 'windows.arm64' }
        '^osx-arm64$' { return 'macos.arm64' }
        '^osx-x64$' { return 'macos.x64' }
        '^linux-x64$' { return 'linux.x64' }
        default { throw "Unsupported RID for manifest platform key: $Rid" }
    }
}

function Get-SbePreviewArtifactBaseName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Version,
        [Parameter(Mandatory)]
        [string] $Rid,
        [Parameter(Mandatory)]
        [ValidateSet('msi', 'dmg', 'tar.gz', 'zip')]
        [string] $Extension
    )

    return "ServiceBusExplorer-$Version-$Rid.$Extension"
}
