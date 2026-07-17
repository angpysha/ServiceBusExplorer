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
- `src/App/Views/Controls/TimeSpanControl.axaml(.cs)` currently renders four permanently visible
  `NumericUpDown` controls, caps days at 36,500, omits milliseconds, and writes `TimeSpan` on every
  component change. It has no Apply/Cancel transaction.
- Legacy `src/ServiceBusExplorer/Controls/Popup.cs` and related WinForms controls depend on Windows
  message/window behavior and are not reusable in Avalonia.
- `SendMessageView` exists, and queue/subscription views bind a `SendMessageViewModel` through
  `ContentControl`, but `src/App/App.axaml` has no corresponding DataTemplate registration.
- The current `SendMessageViewModel` calls `IQueueService.SendAsync(entityPath, message)`.
  `QueueDetailViewModel` supplies the queue path; both `TopicDetailViewModel` and
  `SubscriptionDetailViewModel` supply the topic name. Subscription publishing therefore already
  uses the parent-topic backend path, but the UI/outcome does not explain that distinction.
- `AppBootstrapper` currently calls `Settings.AddToHistory(opts.ConnectionString)` after connection.
  `SettingsService` persists the trimmed full string in `List<string> ConnectionHistory`; its
  comment explicitly says the full value is retained for reconnect. This path is incompatible with
  any distributed internal artifact.

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
- SAS uses an in-memory connection string supplied for the current connection. Optional persistence
  is a separate, explicit, default-off native-vault operation after a successful connection.
- Entra offers `DefaultAzureCredential` for pre-established developer/environment identity and
  `InteractiveBrowserCredential` as an explicit interactive option.
- Optional tenant ID configures the selected identity credential.
- Interactive browser uses an application-owned public-client registration/client ID and local
  redirect URI; the client ID is configuration, not a secret. The UI warns that operations run
  with the signed-in identity's granted permissions.
- The profile records only authentication family, interaction preference, and for opted-in SAS an
  opaque random credential reference; it never records the secret.

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
- Persist SAS in plaintext or an application-managed encrypted file: rejected. Native per-user
  vault persistence is the only approved optional persistence.

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

### R7. Secret-Free Versioned Profile and Native Credential Vault

**Decision**:

- Persist a versioned profile envelope under the current OS-user application data location.
  Allowlisted fields include at most one CSPRNG-generated opaque credential reference.
- Core exposes asynchronous `ICredentialVault` store/retrieve/delete operations plus typed
  `Available`, `Unavailable`, `Locked`, `PermissionDenied`, `ProviderMissing`, `Unsupported`,
  `NotFound`, and `Failure` outcomes.
- App/infrastructure maps that port to Windows Credential Manager, macOS Keychain Services, or
  Linux freedesktop Secret Service through libsecret or a compatible provider.
- No native type, handle, exception, or platform package leaks into Core or ViewModels.
- Vault retrieval failure keeps the profile/reference and prompts for SAS. It never falls back to
  plaintext, app-managed encrypted files, or an in-memory production persistence substitute.
- Saving is off by default. An explicit save creates a random reference only after the vault write
  succeeds. Replacing a saved SAS explicitly upserts the existing reference; a failed or uncertain
  native result keeps the profile/reference and does not claim which value is stored. Profile
  deletion separately asks whether to delete the vault item and reports profile/vault outcomes.
- Microsoft Entra access tokens are outside this vault contract and are never stored by this
  feature.
- On upgrade, detect existing raw-string history, do not deserialize it into profiles, and offer to
  remove it or derive only namespace/entity metadata in memory after explicit review.

**Rationale**: Allowlisting prevents accidental secret persistence, while the OS account’s native
vault supplies access control and lifecycle. Typed failures preserve a useful profile without
misrepresenting reconnect readiness.

**Alternatives considered**:
- Continue storing strings after redaction: rejected because redaction is fragile.
- Automatically migrate raw strings: rejected because parsing/writing can create another secret
  copy and silently retain unintended metadata.
- Application-managed encryption/DPAPI files: rejected because the approved requirement permits
  only named native vaults and no file fallback.
- Delete the profile when vault lookup fails: rejected because unavailability and credential
  deletion are independent from non-secret profile validity.

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

### R11. Build-versus-Package Decision for Native Vault Adapters

**Decision**: Keep `ICredentialVault` package-neutral. No package is approved in Phase 5.
`ktsu.CredentialCache` 1.2.3 is rejected as an implementation candidate for this requirement.
Implementing thin native adapters and evaluating a different/newer library remain alternatives for
the connection-safety slice.

**Evidence and risk review**:

- License: the v1.2.3 repository tag is MIT, acceptable in principle with attribution.
- Framework: NuGet lists net8.0/net9.0 package targets; net10.0 is computed compatibility, not an
  included net10 target.
- Behavior: v1.2.3 README documents `PersistToStorage`, `StoragePath = "credentials.dat"`, and
  application-managed encryption. It does not document Windows Credential Manager, Keychain
  Services, or Secret Service adapters.
- Provenance/maintenance: v1.2.3 was published 2025-05-04; NuGet showed 1,186 downloads at review
  time and no popular GitHub repository usage. It depends on the same publisher's
  `ktsu.AppDataStorage`, `ktsu.StrongPaths`, and `ktsu.StrongStrings`. Native keyring work appeared
  later and has since undergone breaking API changes, indicating an active but recent surface.
- Native/supply chain: newer code P/Invokes `advapi32`, `Security.framework`, and
  `libsecret-1.so.0`; Linux additionally needs a running D-Bus Secret Service. Default/in-memory
  fallback behavior must be disabled or bypassed in production.
- Smoke requirement: any candidate must prove store/retrieve/replace/delete, process restart,
  locked/denied/missing/provider-absent behavior, package launch, and no file creation on all
  supported RIDs before adoption.

**Alternatives considered**:

- Thin first-party adapters: best control over fallback and result mapping, but increases native
  interop, memory handling, and platform test burden.
- A newer `ktsu.CredentialCache`: potentially reduces code, but remains a candidate only after
  pinned-version source/license/dependency review and smoke tests; 1.2.3 evidence cannot justify it.
- Another maintained keyring package: acceptable only under the same review gate.

**Package decision status**: `NO PACKAGE SELECTED — 1.2.3 REJECTED; ADAPTER SPIKE REQUIRED`.
This does not block T001 because credential vault work remains in the later connection-safety
slice.

### R12. Transactional DurationEditor

**Decision**:

- Replace and rename only the modern Avalonia `TimeSpanControl` as `DurationEditor`; never port the
  WinForms `Popup`, `PopupComboBox`, WndProc, or P/Invoke implementation.
- Core owns immutable `DurationValue`, represented as total whole milliseconds from zero through
  `922337203685477` (the largest whole-millisecond value that fits `TimeSpan`), plus strict
  invariant parse/format/component validation.
- Canonical text is `D.HH:MM:SS[.fff]`: days are unpadded one-or-more digits, HH/MM/SS are exactly
  two digits, and `.fff` appears only for non-zero milliseconds.
- `DurationConstraint` is a separate contextual rule carrying Azure property name, minimum,
  maximum, and special-value policy. It can reject Apply but cannot clamp or narrow the shared
  editor range.
- App owns a compact primary text draft and adjacent Edit button. Editing either representation
  creates an isolated draft. The attached Avalonia Flyout/Popup exposes labelled Days, Hours,
  Minutes, Seconds, and Milliseconds inputs.
- Only Apply with valid shared and contextual validation commits. Cancel, Escape, light dismiss,
  focus movement, or validation failure discards/retains draft as appropriate but never mutates the
  bound value; every close restores focus to Edit.
- Direct typing is primary. A structured component MAY use Avalonia `NumericUpDown` with
  `ShowButtonSpinner=False` and `AllowSpin=True`, preserving keyboard Up/Down without visible arrow
  buttons.
- Layout acceptance uses the existing application minimum width of 820 device-independent pixels
  and scale factors 1.0, 1.5, and 2.0. The maximum canonical string has 21 characters and must remain
  visible/selectable; component labels, values, errors, and actions may reflow but never clip or
  overlap.

**Rationale**: The existing control mutates eagerly, loses milliseconds, imposes an arbitrary day
cap, and consumes a wide row with spinner controls. Core parsing makes semantics deterministic and
reusable; App-only presentation follows Avalonia's per-monitor scaling and Flyout model.

**Alternatives considered**:

- Keep the existing four-spinner control and add milliseconds: rejected for eager mutation, width,
  inaccessible abbreviations, and hidden/clipped-value risk.
- Port the WinForms popup: rejected because Windows message/PInvoke behavior violates the
  cross-platform App boundary.
- Use a text field only: rejected because structured labelled input and field-specific errors are
  approved requirements.
- Permanently show five `NumericUpDown` controls: rejected because spinner arrows and fixed narrow
  fields caused the approved UX defect.
- Clamp the editor to each Azure property: rejected because it conflates product representation
  with contextual service validation.

### R13. Send DataTemplate Defect Is Independent

**Decision**: Register `SendMessageViewModel -> SendMessageView` in App composition as part of the
send-message implementation slice. Its test asserts template resolution separately. DurationEditor
work does not own, hide, or opportunistically repair send view registration.

**Rationale**: The missing template prevents an existing view model from resolving, but it has no
duration dependency. Keeping ownership separate avoids making DurationEditor appear to fix the send
surface accidentally.

### R14. First Internal Executable Milestone

**Decision**: The earliest shareable executable is gated after four reviewed changes:

1. explicit dead-letter routing and typed purge confirmation;
2. a first-internal persistence baseline that replaces raw-string history with non-secret profile
   metadata and requires SAS re-entry;
3. Send DataTemplate availability plus truthful actual-destination context while retaining the
   current `IQueueService.SendAsync` backend;
4. DurationEditor replacement across the reviewed inventory of visible queue, topic, and
   subscription duration properties.

The behavior changes remain separately reviewable, but no executable is distributed until their
combined internal-candidate gate passes. Native vault, connection-context restructuring, advanced
messaging/administration, sessions/recovery, and final packaging follow later.

**History staging**:

- first-internal schema has no `CredentialReference` member and serializes only an explicit
  non-secret allowlist;
- successful SAS connection stores profile metadata only; failed/cancelled attempts store no
  credential input;
- selecting a profile never hydrates SAS, and reconnect always requests the full value;
- legacy string-array history is detected without rendering/logging. Startup atomically replaces
  it with reviewed safe metadata or an empty profile envelope and verifies the result. If the raw
  file cannot be sanitized/removed, startup blocks internal use with a safe error;
- credential references and saved-SAS controls are introduced only with the later native-vault
  schema/version and migration.

**Send staging**:

- one App DataTemplate exposes the existing composer in queue, topic, and subscription contexts;
- a typed `SendTargetContext` carries source context, display name, actual destination kind/path,
  and parent-topic explanation;
- queue sends to queue path; topic publishes to topic path; subscription publishes to its parent
  topic path. Focused fakes capture the exact `entityPath`, while UI tests assert pre-submit and
  outcome wording;
- this slice does not introduce a parallel send service or broad message architecture.

**Artifact staging**: `dotnet run` and an optional single-host development publish are sufficient
for internal feedback. The title/About surface must show internal-development status, revision, and
known limitations. No final RID/package/signing/native-vault claim is inferred.

**Alternatives considered**:

- Wait for the full MVP: rejected because it delays review of already reachable high-risk defects.
- Share after T001 alone: rejected because raw credential history, unavailable Send views, and
  broken visible duration editing remain unacceptable.
- Keep raw history under an “internal only” warning: rejected; labeling is not a security control.
- Add temporary plaintext or encrypted-file SAS reconnect: rejected by the no-fallback boundary.
- Pull native-vault work into the milestone: rejected because safe session-only SAS is sufficient
  and avoids coupling the earliest feedback build to an unapproved native adapter.
- Replace the current send backend now: rejected because current constructors already target the
  correct queue/topic paths; the focused need is view resolution and truthful context/outcomes.

## Clarification Resolution

| Planning question | Resolution |
|---|---|
| Identity credential choice | Explicit `DefaultAzureCredential` or interactive browser, with optional tenant |
| Package formats | Self-contained ZIP/TAR artifacts per listed RID |
| OS floors | Windows 10 22H2, macOS 13, Ubuntu 22.04; revalidate at release |
| Bounded retrieval | 100 items by default, cancellable, continuation/refresh visible |
| Confirmation interaction | Typed async confirmation port; Avalonia modal implementation |
| Test architecture | Fake-backed .NET 10 tests; live Azure tests isolated and opt-in |
| Optional SAS storage | Default-off native vault through async `ICredentialVault`; opaque reference only |
| Vault failure | Typed outcome, preserve profile/reference, prompt for SAS, no file fallback |
| Credential package | No selection; `ktsu.CredentialCache` 1.2.3 rejected; adapter spike required |
| Duration range | Whole milliseconds `0..922337203685477`, independent of Azure property limits |
| Duration UI | Transactional Avalonia `DurationEditor`; invariant compact field plus labelled Flyout |
| Scaling baseline | Existing app minimum width 820 DIPs at 1.0/1.5/2.0 |
| Send DataTemplate | Separate send-slice App composition defect; not DurationEditor scope |
| First internal gate | P0 routing + non-secret history + truthful Send + complete visible DurationEditor inventory |
| Internal SAS | Session-only; full re-entry every connection; no reference or vault UI |
| Internal artifact | `dotnet run` or single-host development publish, explicitly labelled internal |

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
- Microsoft Win32 Credential Management API:
  <https://learn.microsoft.com/windows/win32/api/wincred/>
- Apple Keychain Services items and generic passwords:
  <https://developer.apple.com/documentation/security/keychain-items>
- freedesktop Secret Service specification:
  <https://specifications.freedesktop.org/secret-service/latest/>
- libsecret asynchronous store/lookup/clear API:
  <https://gnome.pages.gitlab.gnome.org/libsecret/libsecret-simple-api.html>
- `ktsu.CredentialCache` v1.2.3 release, README, and MIT license:
  <https://github.com/ktsu-dev/CredentialCache/releases/tag/v1.2.3>
- Avalonia NumericUpDown (`ShowButtonSpinner`, `AllowSpin`) and Flyout documentation:
  <https://docs.avaloniaui.net/docs/reference/controls/numericupdown>
- Avalonia Windows high-DPI/per-monitor scaling guidance:
  <https://docs.avaloniaui.net/docs/guides/platforms/windows>
