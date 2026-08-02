# Design: Service Bus emulator integration tests

**Feature**: `specs/001-safe-servicebus-mvp`  
**Tasks**: T031–T035  
**Date**: 2026-07-19

## Goal

Prove Avalonia MVP application services against a **real AMQP + admin** endpoint without
provisioning cloud Service Bus. Primary acceptance uses the official
[Azure Service Bus emulator](https://learn.microsoft.com/en-us/azure/service-bus-messaging/test-locally-with-service-bus-emulator)
via repo-owned Docker Compose. Live Azure remains optional for emulator gaps only.

## Compose topology

| Service | Image | Role |
|---------|-------|------|
| `emulator` | `mcr.microsoft.com/azure-messaging/servicebus-emulator` | AMQP `:5672`, health/admin HTTP `:5300` |
| `sqledge` | `mcr.microsoft.com/azure-sql-edge` | Emulator storage dependency |

Files live under `tests/Integration/emulator/`:

- `docker-compose.yml` — services, ports, volume mount for `Config.json`
- `Config.json` — declarative queues/topics/subscriptions for MVP scenarios
- `.env.example` — `ACCEPT_EULA`, `MSSQL_SA_PASSWORD`, `CONFIG_PATH` (no secrets committed)
- `.env` — local only (gitignored)

## Connection strings

Static emulator SAS shape (key value is the well-known emulator placeholder documented by Microsoft;
do not treat as a production secret):

| Purpose | Host |
|---------|------|
| Messaging (`ServiceBusClient`) | `Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;` |
| Administration (`ServiceBusAdministrationClient`) | `Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;` |

Readiness: poll `GET http://localhost:5300/health` until healthy (or fixture timeout).

## Test project

`tests/Integration/ServiceBusExplorer.IntegrationTests.csproj` (net10.0, xUnit):

- Skipped unless `SBE_INTEGRATION=1`
- `ServiceBusEmulatorFixture` starts or assumes Compose is up, waits for health, builds clients /
  application services (prefer production adapters, not fakes)
- Scenarios:
  - **T032**: browse, send, peek-lock settle, confirmed receive-and-delete, purge outcomes,
    entity/rule lifecycle conflict/refresh
  - **T033**: sessions, deferred, recovery send-before-settle (after R4)

Default `dotnet test` on Unit/Contract/UI must **not** require Docker.

## CI

Optional dedicated workflow job: install Docker, `compose up`, health wait, `SBE_INTEGRATION=1
dotnet test tests/Integration`. Unit/contract jobs stay Docker-free.

## Out of scope / gaps → LiveAzure (T034)

- Entra ID / `TokenCredential` paths
- Real throttling and quota exhaustion
- True RBAC permission denial across tenants
- Behaviors the emulator documents as unsupported (quarantine with explicit skip reason)

## Security

- Never commit `.env` or real passwords
- Never log connection strings, SAS keys, message bodies, or application properties
- Emulator EULA acceptance is a local/CI operator choice via `ACCEPT_EULA=Y`

## Compatibility note

Microsoft states the emulator is not positioned as compatible with the community WinForms Service
Bus Explorer product. This suite targets `Azure.Messaging.ServiceBus` and the administration
client used by the Avalonia MVP; validate and document any emulator limitations in the Integration
README as they are discovered.
