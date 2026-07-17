# Implementation Plan: Safe Service Bus MVP

**Branch**: `feature/avalonia-servicebus-mvp` | **Date**: 2026-07-17 | **Spec**: [spec.md](spec.md)

**Input**: Approved feature specification in `specs/001-safe-servicebus-mvp/spec.md`

## Summary

Evolve the existing .NET 10 Avalonia preview into a safe Service Bus-focused MVP without
removing or restructuring the WinForms product. Preserve the current Core/ViewModels/Services/App
projects, replace ambiguous message sub-queue defaults with an explicit `MessageSource`, add a
framework-neutral confirmation port, and route every connection through one connection-context
factory that honors SAS or `TokenCredential`, scope, tenant, loading options, capabilities, and
lifetime. Optional SAS persistence is mediated by a framework-neutral asynchronous
`ICredentialVault` and native per-user stores; it is disabled by default and settings contain only
an opaque random reference. The modern `TimeSpanControl` is replaced by a transactional Avalonia
`DurationEditor`, while Core owns invariant `DurationValue` semantics. The first implementation
slice remains deliberately limited to the destructive dead-letter routing defect, regression
tests, and confirmation wiring.

## Technical Context

**Language/Version**: C# with nullable reference types on .NET 10

**Primary Dependencies**: Avalonia 11, ReactiveUI, `Azure.Messaging.ServiceBus`,
`Azure.Identity`, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging

**Storage**: Local per-user JSON for versioned, secret-free connection profiles, display
preferences, and an optional opaque random credential reference; optional SAS secrets reside only
in Windows Credential Manager, macOS Keychain Services, or Linux Secret Service/libsecret

**Testing**: New .NET 10 xUnit test project(s) with hand-written fakes around application ports;
Avalonia UI/layout/accessibility tests at 820 device-independent pixels and 100/150/200% scaling;
separate opt-in live Azure acceptance tests

**Target Platform**: Windows 10 22H2+ x64, macOS 13+ x64/arm64, Ubuntu 22.04+ x64 preview
artifacts; floors must be reviewed against the .NET 10/Avalonia support matrix before release

**Project Type**: Cross-platform desktop application retained beside a legacy Windows desktop
application

**Performance Goals**: UI operations begin asynchronously without blocking the dispatcher;
entity/message retrieval is bounded to 100 items per request by default; cancellation is observed
at each Azure call and between batch items

**Constraints**: SAS saving is opt-in and off by default; no plaintext or application-managed
encrypted-file fallback; Entra access tokens are not stored by this feature; no secret or message
content in history or routine logs; no sync-over-async; source-specific destructive actions require
an explicit source and confirmation; no production code for excluded Azure services; no invented
network API; WinForms remains buildable

**Scale/Scope**: One interactive operator, one live connection context at a time, namespace or
single-entity scope, queues/topics/subscriptions/rules, and bounded message batches

## Constitution Check

*GATE: evaluated before Phase 0 and re-evaluated after Phase 1.*

### Pre-design evaluation

| Principle | Result | Design response |
|---|---|---|
| I. Avalonia is the product UI | PASS | All new behavior targets `src/App`; WinForms is retained, not extended into new architecture. |
| II. Preserve layer boundaries | PASS | Core owns models/ports, ViewModels own presentation state, Services own Azure adapters, App owns dialogs/composition/persistence. |
| III. Secure modern Azure integration | PASS | Profiles exclude secrets; optional SAS storage is native-vault-only and Entra tokens are excluded. |
| IV. Tests define completion | PASS | Safety fixes lead with regression tests; vault lifecycle, unit, contract, UI/accessibility, live Azure, and package smoke layers are defined. |
| V. Async, observable, resilient | PASS | All ports accept cancellation; typed outcomes distinguish cancellation, stale state, partial success, and failure. |
| Technical/security constraints | PASS | .NET 10/Avalonia 11/ReactiveUI retained; no unsupported package is required. |
| Workflow/governance | PASS | Existing code was inspected through the project codesearch index and alternatives/migration impact are recorded. |

No constitutional exception or unresolved `NEEDS CLARIFICATION` remains.

## Key Design Decisions

1. **Explicit source type**: replace `MessageSubQueue.None` defaults at application boundaries
   with a non-null `MessageSource` value (`Active`, `DeadLetter`, `TransferDeadLetter`). UI state
   uses `MessageSource?` so “not selected” is representable and destructive commands remain
   disabled. Azure `SubQueue` mapping occurs once in Services and has no default arm.
2. **Confirmation port**: Core defines confirmation request/result semantics; ViewModels depend on
   `IConfirmationService`; App implements an Avalonia modal presenter. The service receives a
   typed target, source, consequence, and risk level rather than a preformatted message.
3. **Authoritative connection context**: a single factory validates a transient connection request,
   creates SAS or identity clients, probes capabilities, and returns an async-disposable live
   context. Profiles and live credentials are separate types.
4. **Authentication default**: Entra uses `DefaultAzureCredential` for pre-established identity and
   an explicit interactive-browser option when requested. Interactive sign-in uses the
   application-owned public-client ID and local redirect URI; optional tenant ID is applied to the
   selected credential. Tokens and credential objects never cross into persistence.
5. **Scope and capabilities**: namespace browsing/administration clients exist only for
   namespace-scoped connections. Entity-scoped connections expose only their declared entity and
   capabilities; unavailable commands are absent or disabled with an explanation.
6. **Operation outcomes**: service exceptions are translated into secret-safe categories:
   validation, authentication, authorization, conflict, throttled, unavailable, cancelled, stale,
   partial, and unknown. Inputs, tokens, connection strings, message bodies, and properties are not
   logged.
7. **Native credential vault**: Core defines async `ICredentialVault` store/retrieve/delete
   operations and typed availability/failure outcomes. App/infrastructure supplies an explicit
   Windows Credential Manager, macOS Keychain Services, or Linux Secret Service/libsecret adapter.
   A CSPRNG-generated opaque reference is the only vault-related profile field. Missing, locked,
   denied, unavailable, provider-missing, or unsupported vault states preserve the profile and
   request SAS again. Replacement and optional cleanup on profile deletion are explicit workflows.
8. **Credential package status**: no third-party package is selected. `ktsu.CredentialCache`
   1.2.3 is rejected because its published API documents app-managed file persistence and predates
   the native-store rewrite. Native adapters and a separately reviewed newer library remain
   alternatives behind `ICredentialVault`; any package requires license, maintenance, native-code,
   supply-chain, fallback, and three-platform smoke approval.
9. **Preview packaging**: self-contained RID artifacts are `win-x64.zip`, `osx-x64.zip`,
   `osx-arm64.zip`, and `linux-x64.tar.gz`. Development previews may be unsigned but must say so.
10. **Duration editor**: rename/replace the modern `TimeSpanControl` with `DurationEditor`; do not
    reuse WinForms `Popup`, `PopupComboBox`, `WndProc`, or P/Invoke code. Core owns non-negative,
    millisecond-precision `DurationValue`, strict invariant `D.HH:MM:SS[.fff]` parsing/formatting,
    component validation, and a separate contextual `DurationConstraint`. App owns a compact
    primary draft field plus Edit affordance and an Avalonia Flyout/Popup with fully labelled
    component inputs. Only Apply commits; Cancel, Escape, light-dismiss, invalid input, and focus
    movement leave the bound value unchanged and restore focus to Edit.
11. **Duration input/layout**: direct typing is primary. App MAY use Avalonia `NumericUpDown` inside
    the structured editor with `ShowButtonSpinner=False` and spinning enabled for keyboard Up/Down;
    permanent spinner arrows are prohibited. The complete maximum representation must fit or
    scroll without hidden digits at the app minimum `MinWidth=820` and at 1.0/1.5/2.0 scaling.
12. **Send view registration**: the missing `DataTemplate` mapping
    `SendMessageViewModel -> SendMessageView` is a separate App composition defect assigned to the
    send-message slice, not to `DurationEditor` work.

Detailed rationale is in [research.md](research.md), models in
[data-model.md](data-model.md), and normative desktop contracts in [contracts/](contracts/).

## Project Structure

### Documentation (this feature)

```text
specs/001-safe-servicebus-mvp/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── application-services.md
│   └── ui-behavior.md
└── checklists/
    ├── requirements.md
    ├── security-safety.md
    └── ux-accessibility.md
```

### Source Code (repository root)

```text
src/
├── Core/                 # Framework-neutral records, DurationValue, state machines, and ports
├── ViewModels/           # ReactiveUI presentation state and commands
├── Services/             # Azure Service Bus adapters and client/context factory
├── App/                  # Avalonia views, composition, dialogs, settings, native vault adapters
├── ServiceBus/           # Existing modern SDK helpers reused where contracts align
├── ServiceBusExplorer/   # Legacy WinForms application retained during preview
└── ServiceBusExplorer.Tests/ # Existing legacy net472 tests

tests/                    # Planned modern net10 test projects
├── Unit/
├── Contract/
├── UI/
└── LiveAzure/
```

**Structure Decision**: extend the current Core/ViewModels/Services/App split. Do not introduce a
parallel architecture or move legacy code. New modern tests are separated from the net472 legacy
suite so .NET 10 contracts and Avalonia behavior can run cross-platform.

## Delivery Slices

1. **Safety regression slice**: introduce explicit `MessageSource`, remove destructive default
   parameters, add fake-backed queue/subscription routing tests, and wire typed confirmation for
   purge. Do not refactor unrelated entity screens.
2. **Connection safety slice**: separate `ConnectionProfile` from transient credentials, migrate
   raw history defensively, add the connection-context factory and `ICredentialVault`, approve a
   native adapter implementation, and honor auth/scope/capabilities. This does not alter T001.
3. **Core administration and messaging**: fill entity/rule lifecycle, send/receive/settlement,
   typed outcomes, bounded retrieval, stale-state handling, and independently register the existing
   `SendMessageView` DataTemplate.
4. **Sessions and selected recovery**: ownership state machine, deferred lookup, send-before-settle
   recovery, diagnostic property treatment, and per-item outcomes.
5. **Accessible preview and packaging**: replace `TimeSpanControl` with the transactional
   `DurationEditor`, verify keyboard/focus/automation and 820-DIP scaling layouts, preserve honest
   service navigation, and produce package launch smoke evidence and preview guidance.

Each slice ends at a human review checkpoint; tasks are produced later by `/speckit.tasks`.

## Post-design Constitution Check

| Check | Result |
|---|---|
| Design preserves approved scope and keeps WinForms available | PASS |
| New UI behavior is Avalonia with business behavior outside views | PASS |
| Azure SDK, identity, and optional native-vault choices are secret-safe | PASS |
| Async/cancellation, retry boundaries, bounded retrieval, and diagnostics are explicit | PASS |
| Safety, vault lifecycle, contract, accessibility, live Azure, and package verification trace to acceptance criteria | PASS |
| Core duration semantics remain framework-neutral and App owns Avalonia interaction/layout | PASS |
| Legacy Popup/PInvoke reuse is prohibited and Send DataTemplate repair remains separate | PASS |
| Alternatives, migration impact, exclusions, and the first implementation slice are explicit | PASS |

**Final design gate assessment**: PASS, subject to mandatory human review. No complexity waiver is
requested. Package selection is intentionally gated rather than unresolved architecture: no
credential package may enter implementation until its review passes.

## Complexity Tracking

No constitutional violations require justification.
