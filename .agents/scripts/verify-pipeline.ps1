# verify-pipeline.ps1 — sanity-check an installed pipeline (pwsh).
# Usage: pwsh -File verify-pipeline.ps1 [TARGET_DIR]
param([string]$Target = (Get-Location).Path)

. (Join-Path $PSScriptRoot 'lib/PipelineScripts.ps1')

$Target = (Resolve-Path $Target).Path
Set-Location $Target

$Errors = 0
$Warnings = 0

function Test-Check {
    param([string]$Label, [string]$Path)
    if (Test-Path $Path) { Write-Host "OK   $Label" }
    else { Write-Host "MISS $Label ($Path)"; $script:Errors++ }
}

function Write-Warn {
    param([string]$Message)
    Write-Host "WARN $Message"
    $script:Warnings++
}

Write-Host "=== Verify pipeline @ $Target ==="
Test-Check 'adapt skill' '.agents/SKILL.md'
Test-Check 'project-intake skill' '.agents/skills/project-intake/SKILL.md'
Test-Check 'spec-kit skill' '.agents/skills/spec-kit/SKILL.md'
Test-Check 'pipeline.home' '.agents/pipeline.home'
Test-Check 'manifest' 'pipeline.manifest.json'
Test-Check 'checkpoint dir' '_code_agent'
Test-Check 'sdlc docs dir' 'docs/sdlc'

$status = '?'
if (Test-Path 'pipeline.manifest.json') {
    $status = Read-ManifestValue -Keys @('status')
    if ([string]::IsNullOrWhiteSpace($status)) { $status = '?' }
    Write-Host "INFO manifest status: $status"
}

if ($status -eq 'needs_adapt') {
    Write-Warn "run 'Run .agents adapt' then 'agentic-tool apply' to reach status: ready"
    if (-not (Test-Path 'PROJECT.md')) {
        Write-Warn 'PROJECT.md missing — adapt will run project-intake (vision + stack) before stack detection'
    }
    if (Test-Path '.agents/pipeline.recommendation.json') {
        Write-Host 'INFO recommendation present (apply pending?)'
    }
}
elseif ($status -eq 'ready') {
    Test-Check 'orchestrator skill' '.agents/skills/sdlc-orchestrator/SKILL.md'
    Test-Check 'coordinator agent' '.agents/agents/coordinator.md'
    Test-Check 'pack registry' '.agents/packs/registry.json'

    if (Test-Path 'pipeline.manifest.json') {
        $manifest = Get-Content 'pipeline.manifest.json' -Raw | ConvertFrom-Json
        foreach ($prop in $manifest.gates.PSObject.Properties) {
            $gatePath = Resolve-GateScriptPath $prop.Value
            if (Test-Path $gatePath) {
                Write-Host "OK   gate script: $gatePath"
            }
            else {
                Write-Host "MISS gate script: $($prop.Value)"
                $Errors++
            }
        }
    }
}

if (Test-Path 'graphify-out') { Write-Host 'OK   graphify-out present' }
else { Write-Host 'INFO graphify-out missing — adapt uses mcp-codebase-search first, then graphify if available' }
if (Test-Path 'pipeline.manifest.json') {
    $cs = Read-ManifestValue -Keys @('code_search', 'codebase_name')
    if (-not [string]::IsNullOrWhiteSpace($cs)) { Write-Host "INFO code_search.codebase_name: $cs" }

    $installedCatalogVersion = Read-ManifestValue -Keys @('catalog_version')
    if ([string]::IsNullOrWhiteSpace($installedCatalogVersion)) {
        $installedCatalogVersion = Read-ManifestValue -Keys @('adapt', 'catalog_version')
    }
    if (-not [string]::IsNullOrWhiteSpace($installedCatalogVersion)) {
        Write-Host "INFO catalog_version (installed): $installedCatalogVersion"
    }

    $catalogVersion = ''
    if (Test-Path '.agents/pipeline.home') {
        $homePath = (Get-Content '.agents/pipeline.home' -Raw).Trim()
        $versionFile = Join-Path $homePath 'VERSION'
        if (Test-Path $versionFile) {
            $catalogVersion = (Get-Content $versionFile -Raw).Trim()
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($catalogVersion)) {
        Write-Host "INFO catalog_version (tool package): $catalogVersion"
        if ($installedCatalogVersion -and $installedCatalogVersion -ne $catalogVersion) {
            Write-Warn "catalog stale — installed $installedCatalogVersion, tool has $catalogVersion. Run: agentic-tool apply && agentic-tool sync --host auto"
        }
    }
    elseif ($status -eq 'ready') {
        Write-Warn 'cannot read catalog VERSION — run agentic-tool apply to refresh skills/agents/rules'
    }

    $specKitEnabled = Read-ManifestValue -Keys @('spec_kit', 'enabled')
    if ($specKitEnabled -eq 'True' -or $specKitEnabled -eq 'true') {
        if (Test-Path '.specify') { Write-Host 'OK   Spec Kit initialized (.specify present)' }
        else { Write-Warn "spec_kit.enabled but .specify missing — run 'specify init . --integration cursor'" }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($specKitEnabled)) {
        Write-Host 'INFO spec_kit.enabled=false — running in SPEC-KIT DEGRADED mode'
    }
}

Write-Host '==============================='
if ($Errors -gt 0) {
    Write-Host "FAIL: $Errors problem(s), $Warnings warning(s)"
    exit 1
}
Write-Host "PASS: pipeline structure OK ($Warnings warning(s))"
