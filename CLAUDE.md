# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

All commands run from the repo root (solution is under `src/`):

```bash
# Restore packages
dotnet restore src/

# Build (Debug)
dotnet build src/

# Build (Release)
dotnet build -c Release src/

# Run all unit tests
dotnet test src/ -c Release

# Run a single test class
dotnet test src/ --filter "FullyQualifiedName~ClassName"
```

Requires Visual Studio 2022 (17.8+) for UI editing. VS must run as DPI-unaware process when editing WinForms designer.

`TreatWarningsAsErrors=true` is set globally — all build warnings are errors.

## Architecture

**9-project solution** (`src/ServiceBusExplorer.sln`):

| Project | Target | Purpose |
|---------|--------|---------|
| `ServiceBusExplorer` | net472 | Main WinForms UI — forms, controls, helpers |
| `Common` | net472 | Shared entity models, abstractions, and `ServiceBusHelper` (core business logic) |
| `ServiceBus` | netstandard2.0 | Service Bus operations (uses `Azure.Messaging.ServiceBus`) |
| `EventHubs` | netstandard2.0 | Event Hubs operations (uses `Azure.Messaging.EventHubs`) |
| `Relay` | netstandard2.0 | Relay services (uses `Microsoft.Azure.Relay`) |
| `NotificationHubs` | net472 | Notification Hubs (uses `Microsoft.Azure.NotificationHubs`) |
| `EventGridExplorerLibrary` | netstandard2.0 | Event Grid support |
| `Utilities` | netstandard2.0 | JSON serialization helpers and shared utilities |
| `ServiceBusExplorer.Tests` | net472 | xUnit tests with FluentAssertions |

**Dependency direction:** `ServiceBusExplorer (UI)` → `Common` → domain libraries (`ServiceBus`, `EventHubs`, etc.) → `Utilities`

Domain libraries target `netstandard2.0` for portability; the UI, Common, and NotificationHubs target `net472`.

**UI pattern:** Traditional WinForms code-behind (no MVVM). Custom `Handle*Control` and `Test*Control` classes in `ServiceBusExplorer/Controls/` encapsulate per-entity-type UI logic. `MainForm.cs` (7,800+ LOC) is the central orchestrator.

**Dual SDK situation:** The project is migrating from the deprecated `WindowsAzure.ServiceBus` SDK to the modern `Azure.Messaging.*` family. `ServiceBus.csproj` already uses the modern SDK; `Common` still references the old one. New code should use the modern SDKs and `Azure.Identity` for authentication (Entra ID / `DefaultAzureCredential`).

## C# Coding Standards

- Prefer modern C#: records, pattern matching, nullable reference types
- Use `async`/`await` correctly — avoid sync-over-async (`Task.Result`, `.Wait()`)
- Document public APIs with XML doc comments
- Line endings: CRLF; indentation: 4 spaces for `.cs`, 2 spaces for `.config`
