# Service Bus emulator (integration tests)

Repo-owned Compose for Spec Kit tasks **T031–T033**. Design:
[`docs/sdlc/design/service-bus-emulator-integration-tests.md`](../../../docs/sdlc/design/service-bus-emulator-integration-tests.md).

## Prerequisites

- Docker Desktop / Docker Engine with Compose v2
- ~2 GB RAM free

## Start

```bash
cd tests/Integration/emulator
cp .env.example .env
# Edit .env: ACCEPT_EULA=Y and a strong MSSQL_SA_PASSWORD
docker compose up -d
curl -sf http://localhost:5300/health
```

## Run tests (T031)

From repo root (PowerShell 7+):

```powershell
pwsh ./scripts/run-integration-tests.ps1
```

Or manually:

```bash
cd tests/Integration/emulator
cp .env.example .env
# Edit .env: ACCEPT_EULA=Y and a strong MSSQL_SA_PASSWORD
docker compose up -d
curl -sf http://localhost:5300/health
SBE_INTEGRATION=1 dotnet test tests/Integration/ -c Release
```

Without `SBE_INTEGRATION=1`, Integration tests must skip (default local/CI unit jobs stay fast).

## Connection strings

Messaging:

```text
Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
```

Administration (port 5300):

```text
Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
```

Requires **Azure.Messaging.ServiceBus ≥ 7.20** so the administration client honors the `:5300` port
(older 7.19 builds connect to `:80` and fail).

## Stop

```bash
docker compose down
```

## Entities

See `Config.json`: `mvp.queue.active`, `mvp.queue.sessions`, `mvp.topic` / `mvp.subscription`.
