#Requires -Version 7.0
<#
.SYNOPSIS
  Read/write preview MANIFEST.txt (key=value) per artifact-manifest contract.
#>

Set-StrictMode -Version Latest

$PackagingCommonPath = Join-Path $PSScriptRoot 'PackagingCommon.ps1'
. $PackagingCommonPath

function ConvertFrom-SbePreviewManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (-not (Test-Path $Path)) {
        throw "MANIFEST not found: $Path"
    }

    $map = [ordered]@{}
    foreach ($line in Get-Content -Path $Path) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }
        $idx = $trimmed.IndexOf('=')
        if ($idx -lt 1) {
            throw "Invalid MANIFEST line (expected key=value): $trimmed"
        }
        $key = $trimmed.Substring(0, $idx).Trim()
        $value = $trimmed.Substring($idx + 1).Trim()
        $map[$key] = $value
    }
    return $map
}

function Write-SbePreviewManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [System.Collections.IDictionary] $Entries,

        [switch] $MergeExisting
    )

    $map = [ordered]@{}
    if ($MergeExisting -and (Test-Path $Path)) {
        $existing = ConvertFrom-SbePreviewManifest -Path $Path
        foreach ($k in $existing.Keys) {
            $map[$k] = $existing[$k]
        }
    }

    foreach ($k in $Entries.Keys) {
        $map[[string]$k] = [string]$Entries[$k]
    }

    # Stable key order: product/version/preview first, then sorted remainder.
    $priority = @('product', 'version', 'preview', 'configuration')
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($p in $priority) {
        if ($map.Contains($p)) {
            $lines.Add("$p=$($map[$p])")
            $map.Remove($p)
        }
    }
    foreach ($k in ($map.Keys | Sort-Object)) {
        $lines.Add("$k=$($map[$k])")
    }

    $dir = Split-Path $Path -Parent
    if ($dir -and -not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    Set-Content -Path $Path -Value ($lines -join [Environment]::NewLine) -Encoding utf8
    return $Path
}

function Set-SbePreviewManifestArtifact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ManifestPath,

        [Parameter(Mandatory)]
        [string] $PlatformKey,

        [Parameter(Mandatory)]
        [string] $ArtifactFileName,

        [Parameter(Mandatory)]
        [string] $Sha256,

        [Parameter(Mandatory)]
        [string] $Signing,

        [Parameter(Mandatory)]
        [string] $Notarization,

        [string] $Product = 'Service Bus Explorer',
        [string] $Version = (Get-SbeAppVersion),
        [string] $Preview = 'true'
    )

    $entries = [ordered]@{
        product                              = $Product
        version                              = $Version
        preview                              = $Preview
        "artifact.$PlatformKey"              = $ArtifactFileName
        "sha256.$PlatformKey"                = $Sha256.ToLowerInvariant()
        "signing.$PlatformKey"               = $Signing
        "notarization.$PlatformKey"          = $Notarization
    }

    return Write-SbePreviewManifest -Path $ManifestPath -Entries $entries -MergeExisting
}

function Test-SbePreviewManifestSchema {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [System.Collections.IDictionary] $Manifest,

        [string[]] $RequiredPlatformKeys = @()
    )

    $errors = [System.Collections.Generic.List[string]]::new()

    foreach ($required in @('product', 'version', 'preview')) {
        if (-not $Manifest.Contains($required) -or [string]::IsNullOrWhiteSpace([string]$Manifest[$required])) {
            $errors.Add("Missing required key: $required")
        }
    }

    if ($Manifest.Contains('preview') -and [string]$Manifest['preview'] -ne 'true') {
        $errors.Add("preview must be 'true' for preview packages (got '$($Manifest['preview'])')")
    }

    foreach ($platform in $RequiredPlatformKeys) {
        foreach ($prefix in @('artifact', 'sha256', 'signing', 'notarization')) {
            $key = "$prefix.$platform"
            if (-not $Manifest.Contains($key) -or [string]::IsNullOrWhiteSpace([string]$Manifest[$key])) {
                $errors.Add("Missing required platform key: $key")
            }
        }

        $shaKey = "sha256.$platform"
        if ($Manifest.Contains($shaKey)) {
            $sha = [string]$Manifest[$shaKey]
            if ($sha -notmatch '^[0-9a-f]{64}$') {
                $errors.Add("sha256.$platform must be 64 lowercase hex chars")
            }
        }
    }

    if ($Manifest.Contains('signing.windows.x64') -and [string]$Manifest['signing.windows.x64'] -ne 'unsigned') {
        $errors.Add("signing.windows.x64 must be 'unsigned' for this feature")
    }

    if ($Manifest.Contains('notarization.macos.arm64') -and [string]$Manifest['notarization.macos.arm64'] -eq 'notarized') {
        if (-not $Manifest.Contains('signing.macos.arm64') -or [string]$Manifest['signing.macos.arm64'] -ne 'developer-id') {
            $errors.Add("notarization.macos.arm64=notarized requires signing.macos.arm64=developer-id")
        }
    }

    return [pscustomobject]@{
        Ok     = ($errors.Count -eq 0)
        Errors = $errors.ToArray()
    }
}
