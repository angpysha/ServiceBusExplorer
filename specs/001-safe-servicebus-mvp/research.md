# Research: Safe Service Bus MVP

**Feature**: [spec.md](spec.md)  
**Plan**: [plan.md](plan.md)  
**Date**: 2026-07-17

All planning unknowns are resolved below. Existing implementation claims come from the project
codesearch index; API choices were checked against current Microsoft Learn and Avalonia
documentation.

## Existing Implementation Findings

- The modern application already targets .NET 10 and is split into `Core`, `ViewModels`,
  `Services`, and `App`.
- `src/Core/Contracts/Connection.cs` combines a full connection string with authentication and
  scope options. `src/App/AppBootstrapper.cs` currently constructs both clients from that string,
  regardless of selected authentication mode, and persists it through `Settings.AddToHistory`.
- `src/Core/Contracts/IQueueService.cs` defaults message operations to
  `MessageSubQueue.None`; queue and subscription view models initialize their selected sub-queue
  to `None`. This makes active routing indistinguishable from “not explicitly selected.”
- `src/Services/ServiceBus/QueueService.cs` maps every unrecognized value to Azure
  `SubQueue.None`, including purge, and purges by `ReceiveAndDelete`.
- Existing modern SDK helpers already demonstrate both `ServiceBusClient(connectionString)` and
  `ServiceBusClient(fullyQualifiedNamespace, TokenCredential)` creation. They are evidence to
  reuse behavior, not a reason to couple the new Avalonia composition to legacy helpers.
- No Avalonia confirmation abstraction or `AutomationProperties` metadata was found in the
  indexed modern views. App composition currently registers Relay and Event Hubs even though they
  are excluded from this MVP.

## Decisions

### R1. Evolve the Current Four-Layer Architecture

**Decision**: Keep Core/ViewModels/Services/App and improve existing contracts in place.

**Rationale**: It already follows the constitution’s intended dependency flow. A parallel feature
architecture would duplicate service contracts and increase migration risk.

**Alternatives considered**:
- Rebuild as vertical feature modules: rejected for broad churn before the safety fix.
- Reuse WinForms helpers directly from Avalonia: rejected because it imports legacy UI/global
  assumptions and old SDK dependencies.

### R2. One Async-Disposable Connection Context Factory

**Decision**: Replace ad hoc client construction with one `IConnectionContextFactory`. It accepts a
transient `ConnectionRequest`, validates auth and scope, creates one cached `ServiceBusClient` and
an administration client only when permitted, probes selected capabilities, and returns an
`IAsyncDisposable` `LiveConnectionContext`.

**Rationale**: Current Microsoft guidance says Service Bus client types are safe to cache and
should be long-lived. One owner prevents mismatched credentials, scope, and disposal.

**Alternatives considered**:
- Construct clients inside each service: rejected due to inconsistent auth/scope and connection
  churn.
- Promote the existing `ServiceBusHelper2` to application composition: rejected because it belongs
  to a different migration seam and does not model profile/capability boundaries.

### R3. SAS and Entra Credential Selection

**Decision**:
- SAS uses an in-memory connection string supplied for the current connection only.
- Entra offers `DefaultAzureCredential` for pre-established developer/environment identity and
  `InteractiveBrowserCredential` as an explicit interactive option.
- Optional tenant ID configures the selected identity credential.
- Interactive browser uses an application-owned public-client registration/client ID and local
  redirect URI; the client ID is configuration, not a secret. The UI warns that operations run
  with the signed-in identity's granted permissions.
- The profile records only authentication family and interaction preference.

**Rationale**: Azure Service Bus supports client construction with either connection strings or
fully qualified namespace plus `TokenCredential`. Explicit interactive selection avoids an
unexpected browser prompt from a broad credential chain. Current Azure Identity documentation
identifies interactive browser as a desktop/local-development credential, supports tenant/client
configuration, and recommends an application registration rather than the shared development
application.

**Alternatives considered**:
- Interactive browser only: rejected because CLI/IDE/brokered identity is useful and often already
  authenticated.
- `DefaultAzureCredential` only: rejected because failure can be opaque and an evaluator needs an
  intentional interactive path.
- Persist encrypted SAS secrets: rejected by approved MVP scope.

### R4. Explicit Message Source with No Sentinel Default

**Decision**: Core defines `MessageSource` as exactly `Active`, `DeadLetter`, or
`TransferDeadLetter`. Application service methods require it. View models hold
`MessageSource? SelectedSource`, initially null, and source-specific destructive command predicates
require a value. Services exhaustively map source to Azure `SubQueue`.

**Rationale**: `MessageSubQueue.None` is a valid Azure active source, not a safe “unselected”
sentinel. Removing optional parameters makes accidental fallback a compile-time failure.

**Alternatives considered**:
- Add `Unspecified` to the same enum: rejected because it can leak through service boundaries.
- Keep defaults and validate only in UI: rejected because tests or future callers can bypass UI.

### R5. Typed Confirmation Outside UI Concerns

**Decision**: ViewModels invoke `IConfirmationService.ConfirmAsync(ConfirmationRequest, ct)`.
Core owns typed operation/target/source/consequence/risk semantics. App owns Avalonia window
presentation and accessible focus restoration.

**Rationale**: Avalonia documentation recommends a dialog service when view models must request a
dialog without a `Window` dependency. Typed data allows deterministic tests and consistent wording.

**Alternatives considered**:
- ReactiveUI Interaction: viable, but rejected as the sole domain-facing contract because it
  couples all confirmation semantics to ReactiveUI and makes non-UI callers awkward.
- Pass delegates from views: rejected due to lifecycle and consistency problems.

### R6. Bounded Retrieval and Retry Defaults

**Decision**:
- Default page/batch size: 100; user may request a smaller positive amount.
- A single receive wait defaults to one second for purge/empty detection, but timeout is injectable
  in tests.
- Azure SDK retry policy remains the base transport retry. Multi-item destructive and recovery
  orchestration never automatically repeats an item after an acknowledged success.
- Cancellation is passed to every Azure call and checked between items.

**Rationale**: This bounds memory and uncertain work while preserving SDK-supported transient retry.

**Alternatives considered**:
- Unbounded list/load: rejected by the specification.
- Application-level retry of entire batches: rejected because it can duplicate destructive work.

### R7. Secret-Free Versioned Profile Storage

**Decision**: Persist a versioned profile envelope under the current OS-user application data
location. Allowlisted fields are serialized; credentials and message content have no serializable
members. On upgrade, detect existing raw-string history, do not deserialize it into profiles, and
offer to remove it or derive only namespace/entity metadata in memory after explicit review.

**Rationale**: Allowlisting prevents accidental secret persistence. Corrupt history must not block
new connections.

**Alternatives considered**:
- Continue storing strings after redaction: rejected because redaction is fragile.
- Automatically migrate raw strings: rejected because parsing/writing can create another secret
  copy and silently retain unintended metadata.

### R8. Testing Boundaries

**Decision**: Add .NET 10 unit/contract/UI test projects using hand-written fakes for Core ports and
Azure adapter seams. Keep live Azure tests opt-in, separately tagged, and configured only through
environment identity and ephemeral resource names.

**Rationale**: Azure SDK concrete clients are difficult to fake reliably. Application ports make
routing, confirmation, cancellation, and partial outcomes deterministic without mocking SDK
internals.

**Alternatives considered**:
- Mock SDK concrete clients: rejected as brittle.
- Live-only tests: rejected as slow, permission-dependent, and unsuitable for every change.

### R9. Preview Packaging and OS Floors

**Decision**: Produce self-contained `win-x64.zip`, `osx-x64.zip`, `osx-arm64.zip`, and
`linux-x64.tar.gz` artifacts. Initial preview floors are Windows 10 22H2, macOS 13, and Ubuntu
22.04. Each artifact carries version, preview label, RID, checksum, extraction/launch instructions,
known limitations, and signing/notarization status.

**Rationale**: Portable archives minimize installer scope while proving each OS build. Floors are
conservative preview baselines and must be revalidated against currently supported .NET 10 and
Avalonia runtime matrices at release.

**Alternatives considered**:
- MSI/DMG/distribution packages in MVP: deferred because signing, notarization, and installer
  lifecycle are separate release work.
- Framework-dependent packages: rejected for first-evaluator friction.

### R10. Excluded Services

**Decision**: Avalonia navigation and composition omit Relay, Event Grid, Event Hubs, Notification
Hubs, generators, and monitoring features from this preview. There are no visible no-op pages.

**Rationale**: Honest absence matches the approved scope. The separate WinForms application
remains the fallback.

## Clarification Resolution

| Planning question | Resolution |
|---|---|
| Identity credential choice | Explicit `DefaultAzureCredential` or interactive browser, with optional tenant |
| Package formats | Self-contained ZIP/TAR artifacts per listed RID |
| OS floors | Windows 10 22H2, macOS 13, Ubuntu 22.04; revalidate at release |
| Bounded retrieval | 100 items by default, cancellable, continuation/refresh visible |
| Confirmation interaction | Typed async confirmation port; Avalonia modal implementation |
| Test architecture | Fake-backed .NET 10 tests; live Azure tests isolated and opt-in |

No `NEEDS CLARIFICATION` item remains.

## Official Documentation Consulted

- Microsoft Learn, Azure Service Bus client authentication and long-lived client guidance:
  <https://learn.microsoft.com/azure/service-bus-messaging/service-bus-migrate-azure-credentials>
- Microsoft Learn, Service Bus `SubQueue` and dead-letter addressing:
  <https://learn.microsoft.com/dotnet/api/azure.messaging.servicebus.subqueue>
- Microsoft Learn, Azure Identity for .NET and interactive browser options:
  <https://learn.microsoft.com/dotnet/api/overview/azure/identity-readme>
- Avalonia documentation, dialog services and desktop application lifetime:
  <https://docs.avaloniaui.net/docs/how-to/dialogs-how-to>
