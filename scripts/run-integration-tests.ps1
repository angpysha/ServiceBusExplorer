#Requires -Version 7.0
<#
.SYNOPSIS
  Start the Service Bus emulator (Docker Compose) and run opt-in integration tests.

.DESCRIPTION
  Brings up tests/Integration/emulator via docker compose, waits for the health endpoint,
  sets SBE_INTEGRATION=1, and runs tests/Integration/ServiceBusExplorer.IntegrationTests.csproj.

  Requires Docker Compose v2, .NET 10 SDK, and PowerShell 7+ (pwsh).
  See tests/Integration/emulator/README.md and docs/sdlc/design/service-bus-emulator-integration-tests.md.

.PARAMETER Configuration
  dotnet build configuration (default Release).

.PARAMETER Filter
  Optional dotnet test filter (e.g. FullyQualifiedName~EmulatorConnectivity).

.PARAMETER SkipCompose
  Assume the emulator is already running; only wait for health and run tests.

.PARAMETER KeepRunning
  Do not run docker compose down after tests (default: tear down when this script started compose).

.PARAMETER HealthTimeoutSeconds
  Maximum seconds to wait for http://localhost:5300/health (default 180).

.PARAMETER NoBuild
  Pass --no-build to dotnet test.

.EXAMPLE
  pwsh ./scripts/run-integration-tests.ps1

.EXAMPLE
  pwsh ./scripts/run-integration-tests.ps1 -Filter "FullyQualifiedName~EmulatorConnectivity" -KeepRunning
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Filter = '',
    [switch] $SkipCompose,
    [switch] $KeepRunning,
    [int] $HealthTimeoutSeconds = 180,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    $dir = $PSScriptRoot
    while ($dir) {
        if (Test-Path (Join-Path $dir 'specs/001-safe-servicebus-mvp/tasks.md')) {
            return (Resolve-Path $dir).Path
        }
        $parent = Split-Path $dir -Parent
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    throw 'Could not locate repository root (expected specs/001-safe-servicebus-mvp/tasks.md).'
}

function Ensure-EmulatorEnvFile {
    param([string] $EmulatorDir)

    $envFile = Join-Path $EmulatorDir '.env'
    $example = Join-Path $EmulatorDir '.env.example'
    $configPath = Join-Path $EmulatorDir 'Config.json'

    if (-not (Test-Path $configPath)) {
        throw "Missing emulator Config.json at $configPath"
    }

    if (Test-Path $envFile) {
        return
    }

    if (-not (Test-Path $example)) {
        throw "Missing .env.example at $example"
    }

    Write-Host "Creating $envFile from .env.example (local only; gitignored)."
    $content = Get-Content $example -Raw
    $absoluteConfig = (Resolve-Path $configPath).Path
    $content = $content -replace '(?m)^CONFIG_PATH=.*$', "CONFIG_PATH=$absoluteConfig"
    if ($content -notmatch '(?m)^ACCEPT_EULA=Y\s*$') {
        $content = $content -replace '(?m)^ACCEPT_EULA=.*$', 'ACCEPT_EULA=Y'
        Write-Warning 'Set ACCEPT_EULA=Y in .env — ensure you accept the emulator and SQL Edge EULAs.'
    }
    if ($content -match '(?m)^MSSQL_SA_PASSWORD:\s*""\s*$' -or $content -match '(?m)^MSSQL_SA_PASSWORD=\s*$') {
        throw 'Set MSSQL_SA_PASSWORD in tests/Integration/emulator/.env before first run (see .env.example).'
    }
    Set-Content -Path $envFile -Value $content -NoNewline
}

function Wait-EmulatorHealth {
    param(
        [int] $TimeoutSeconds,
        [string] $HealthUrl = 'http://localhost:5300/health'
    )

    Write-Host "Waiting for emulator health at $HealthUrl (timeout ${TimeoutSeconds}s)..."
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = $null

    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $HealthUrl -Method Get -TimeoutSec 5 -SkipHttpErrorCheck
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                Write-Host 'Emulator health check passed.'
                return
            }
            $lastError = "HTTP $($response.StatusCode)"
        }
        catch {
            $lastError = $_.Exception.Message
        }
        Start-Sleep -Seconds 3
    }

    throw "Emulator did not become healthy within ${TimeoutSeconds}s. Last error: $lastError"
}

function Test-DockerAvailable {
    $null = Get-Command docker -ErrorAction Stop
    docker compose version *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'docker compose is not available. Install Docker Desktop / Compose v2.'
    }
}

$repoRoot = Get-RepoRoot
Set-Location $repoRoot

$emulatorDir = Join-Path $repoRoot 'tests/Integration/emulator'
$project = Join-Path $repoRoot 'tests/Integration/ServiceBusExplorer.IntegrationTests.csproj'

if (-not (Test-Path $project)) {
    throw "Integration test project not found: $project (complete T031 first)."
}

$startedCompose = $false

try {
    if (-not $SkipCompose) {
        Test-DockerAvailable
        Ensure-EmulatorEnvFile -EmulatorDir $emulatorDir

        Push-Location $emulatorDir
        try {
            Write-Host 'Starting Service Bus emulator (docker compose up -d)...'
            docker compose up -d
            if ($LASTEXITCODE -ne 0) {
                throw "docker compose up failed with exit code $LASTEXITCODE"
            }
            $startedCompose = $true
        }
        finally {
            Pop-Location
        }
    }

    Wait-EmulatorHealth -TimeoutSeconds $HealthTimeoutSeconds

    $env:SBE_INTEGRATION = '1'

    $testArgs = @(
        'test',
        $project,
        '-c', $Configuration,
        '--verbosity', 'minimal'
    )
    if ($Filter) {
        $testArgs += @('--filter', $Filter)
    }
    if ($NoBuild) {
        $testArgs += '--no-build'
    }

    Write-Host "Running: dotnet $($testArgs -join ' ')"
    & dotnet @testArgs
    $exitCode = $LASTEXITCODE
}
finally {
    Remove-Item Env:SBE_INTEGRATION -ErrorAction SilentlyContinue

    if ($startedCompose -and -not $KeepRunning) {
        Push-Location $emulatorDir
        try {
            Write-Host 'Stopping Service Bus emulator (docker compose down)...'
            docker compose down
        }
        finally {
            Pop-Location
        }
    }
}

if ($exitCode -ne 0) {
    Write-Error "Integration tests failed (exit $exitCode)."
    exit $exitCode
}

Write-Host 'Integration tests passed.'
exit 0
