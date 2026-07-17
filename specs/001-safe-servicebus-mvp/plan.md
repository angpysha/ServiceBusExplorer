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
`DurationEditor`, while Core owns invariant `DurationValue` semantics. The earliest distributable
internal milestone is intentionally narrower than the MVP: it combines reviewed P0 dead-letter
safety, truthful queue/topic/subscription Send availability through the current backend path,
complete visible-form DurationEditor replacement, and a no-secret connection-history baseline.
Native-vault reconnect and the broader connection architecture follow only after this milestone.

## Technical Context

**Language/Version**: C# with nullable reference types on .NET 10

**Primary Dependencies**: Avalonia 11, ReactiveUI, `Azure.Messaging.ServiceBus`,
`Azure.Identity`, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging

**Storage**: First internal version: local per-user JSON containing only versioned non-secret
profile metadata, with no credential reference and SAS re-entered for every connection. Later MVP:
an optional opaque random credential reference may address SAS held only in Windows Credential
Manager, macOS Keychain Services, or Linux Secret Service/libsecret

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
network API; WinForms remains buildable. Before first-internal distribution, raw history writes are
removed, credential references and saved-SAS reconnect are unavailable, and internal labeling
cannot compensate for insecure persistence

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
| IV. Tests define completion | PASS | Four first-internal evidence groups gate sharing; later vault lifecycle, unit, contract, live Azure, and package smoke layers remain defined. |
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
   context. Profiles and live credentials are separate types. This is the post-internal target
   architecture; the internal milestone removes raw persistence from the current bootstrap path
   without making the broader factory refactor a prerequisite.
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
13. **First-internal history floor**: replace `List<string>`/`AddToHistory(connectionString)` with
    an allowlisted, versioned profile writer before any internal artifact leaves the development
    environment. The internal schema contains label, namespace endpoint, auth mode, optional tenant
    and entity scope, and non-sensitive preferences only. It has no credential or credential-
    reference field. Legacy raw entries are never rendered, copied, logged, or used for reconnect;
    startup must atomically overwrite them with reviewed non-secret metadata or remove them, and
    must fail closed if sanitization cannot complete.
14. **First-internal SAS behavior**: the full SAS connection string exists only in the current
    connection request/live client. Selecting a profile restores metadata but leaves SAS empty.
    Every initial, repeat, and post-restart SAS connection requires full re-entry. No vault toggle,
    `ICredentialVault` call, reference generation, or saved-SAS claim is present in this milestone.
15. **Focused Send extension**: preserve the current `IQueueService.SendAsync(entityPath, message)`
    backend. Queue passes its queue path; topic passes its topic path; subscription also passes its
    parent topic path. A typed `SendTargetContext` distinguishes requested context from actual
    publish destination so the subscription page and outcome state “publishes to parent topic”
    before and after the attempt.
16. **Internal artifact status**: the milestone may run via `dotnet run` or a single-host
    development publish. It must display an **Internal development build** label, revision, and
    limitations. This is not evidence for final RID packaging, signing, native-vault, or preview
    release gates.
17. **Universal numeric-input boundary**: `DurationEditor` is the universal reusable control for
    whole-millisecond durations and receives a contextual `DurationConstraint` for each property.
    Relative Send scheduling moves from integer minutes to that duration model. Counts and sizes
    remain `NumericUpDown` values and share one App-level presentation pattern with adequate width,
    integer formatting, visible spinner controls, explicit per-field limits/increments, labels, and
    automation help. Iconic add actions are not steppers and receive action-specific automation
    names rather than numeric behavior.
18. **Current Send guidance**: retain the existing draft and backend contract while adding visible
    helper text plus automation name/help/required semantics to every current input. Body and
    message count are required; session ID is conditionally required by session-enabled
    destinations; schedule delay is required only when scheduling is enabled; all other exposed
    system/application properties remain optional. The page names absent richer properties as
    deferred rather than presenting unavailable controls.

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

1. **Internal P0 routing slice**: introduce explicit `MessageSource`, remove destructive defaults,
   add fake-backed queue/subscription routing tests, and wire typed purge confirmation. Stop for
   human review; no internal distribution yet.
2. **Internal persistence safety baseline**: disable the raw connection-string history writer,
   persist only the first-internal profile allowlist, sanitize/remove legacy raw records, require
   SAS re-entry, and add canary/disk-inspection tests. Stop for security-focused human review; no
   internal distribution yet.
3. **Internal Send availability slice**: register `SendMessageViewModel -> SendMessageView`, retain
   the current queue/topic backend path, add explicit actual-destination context/outcomes, and prove
   queue, topic, and subscription-parent-topic behavior with focused tests.
4. **Internal DurationEditor slice**: replace every inventoried modern Service Bus duration control,
   then pass Core unit and Avalonia transaction/layout/keyboard/accessibility regressions.
5. **First internal candidate gate**: run the combined focused suite, inspect settings/history,
   exercise development-run or single-host artifact startup, and verify internal labeling. Human
   approval is required before sharing the executable.
6. **Native vault and connection architecture**: add `ICredentialVault`, approve native adapters,
   introduce credential references/saved-SAS reconnect, and complete auth/scope/capability factory
   work without weakening the internal profile baseline.
7. **Broader administration and messaging**: complete entity/rule lifecycle, bounded browse,
   receive/settlement, typed outcomes, and stale-state handling.
8. **Sessions and selected recovery**: ownership state, deferred lookup, send-before-settle
   recovery, diagnostic property treatment, and per-item outcomes.
9. **Final accessibility and preview packaging**: complete P1-wide accessibility, self-contained
   RID artifacts, native-vault package smoke, signing-status evidence, and public preview guidance.

Each slice ends at a human review checkpoint. Existing `tasks.md` is intentionally untouched in
this Phase 5 amendment and must be realigned by the later `/speckit.tasks`/analysis workflow before
implementation.

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
| First internal distribution is blocked on secret-free history with no credential reference | PASS |
| Internal Send reuses the current backend while exposing truthful actual destination | PASS |
| Native vault, broad connection refactor, advanced workflows, and final packaging remain later | PASS |
| Alternatives, migration impact, exclusions, and the first implementation slice are explicit | PASS |

**Final design gate assessment**: PASS, subject to mandatory human review. No complexity waiver is
requested. The first internal candidate has its own gate and is not a preview release. Package
selection remains intentionally gated: no credential package may enter implementation until its
review passes.

## Complexity Tracking

No constitutional violations require justification.
