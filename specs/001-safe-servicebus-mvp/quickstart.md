# Validation Quickstart: Safe Service Bus MVP

This guide defines runnable validation after implementation. It does not authorize production code
before tasks and human review.

## Prerequisites

- .NET 10 SDK
- Representative Azure Service Bus namespace with isolated test queues/topics/subscriptions
- SAS policy and Entra identity with intentionally varied permissions
- Session-enabled, dead-letter, transfer-dead-letter, and deferred test fixtures
- Windows 10 22H2+, macOS 13+, and Ubuntu 22.04+ validation hosts
- For the later native-vault gate only: current-user Windows Credential Manager, macOS login
  Keychain, and a Linux desktop session with libsecret plus an active Secret Service provider

Never place credentials or message content in source, test snapshots, command history, or logs.
Live tests acquire identity from the environment or receive SAS through a secret environment
variable.

## First Internal Candidate Gate

The earliest executable that may be shared internally is not a final preview package. Run from
source with `dotnet run --project src/App/App.csproj` or use a reviewed single-host development
publish. The title/About surface must identify **Internal development build**, revision, and known
limitations.

The gate is cumulative:

1. Pass Scenario 1 dead-letter routing and typed purge confirmation.
2. Seed settings with a synthetic canary SAS string, start the app, and verify raw history is
   atomically sanitized/removed. Repeat successful, failed, cancelled, repeat, and restarted SAS
   connections.
3. Open Send from queue, topic, and subscription contexts. Verify queue path, topic path, and
   subscription parent-topic path through the focused fake/contract suite and visible outcomes.
4. Complete Scenario 6 for every duration field in the reviewed queue/topic/subscription visible-
   form inventory.
5. Inspect settings, history, logs, screenshots, and test attachments; then review the internal
   label and limitations.

Expected:

- absent source cannot purge; dead-letter purge never touches active messages; cancellation makes
  zero service-changing calls;
- history contains only non-secret profile metadata and no credential-reference property;
- every SAS connection asks for the full value and no saved-SAS/vault control appears;
- queue/topic Send resolves the composer, and subscription says it publishes to the parent topic
  before submission and in success/failure output;
- all inventoried visible duration fields use `DurationEditor` and pass unit/UI/layout/accessibility
  regressions;
- no executable is shared until all four behavior/security areas receive human approval.

## Build and Automated Checks

From repository root:

```bash
dotnet restore src/
dotnet build src/ --configuration Release --verbosity minimal
dotnet test src/ --configuration Release
```

After modern test projects exist, run their unit and contract suites on every platform. Run
`LiveAzure` tests only when the explicit opt-in environment flag and isolated namespace are set.

Expected outcome:

- warnings are treated as errors;
- safety routing, confirmation, profile serialization, default-off vault behavior, native vault
  lifecycle/failures, auth selection, settlement eligibility, session loss, recovery partial
  failure, accessibility semantics, and duration round-trip pass;
- default test execution does not contact Azure.

## Scenario 1: Destructive DLQ Regression

1. Prepare active and dead-letter messages on a queue and subscription.
2. Launch the Avalonia preview and navigate to each message view.
3. Observe that no source is initially selected and purge is unavailable.
4. Select dead-letter, invoke purge, inspect the confirmation target/source/consequence, and cancel.
5. Repeat and confirm.

Expected:

- cancellation changes nothing;
- confirmed purge affects dead-letter only;
- active messages remain;
- operation output identifies dead-letter without message content;
- unavailable transfer dead-letter never falls back.

This is the required first implementation slice. It is necessary but not sufficient for internal
distribution.

## Scenario 2: Optional Native-Vault SAS Storage (Post-Internal)

On each supported OS:

1. Connect with SAS while leaving the save toggle at its default off state; restart and verify SAS
   is requested again.
2. Explicitly enable saving, connect successfully, restart, and reconnect through the designated
   native store.
3. Inspect settings/history and verify only a high-entropy opaque reference is present.
4. Lock/deny/stop the vault provider where the platform permits, then attempt reconnect.
5. Delete the native item externally and attempt reconnect.
6. Enter SAS manually and decline replacement; then explicitly replace the saved SAS and reconnect.
7. Remove a referenced profile once without cleanup and once with cleanup; exercise cleanup failure.
8. Connect with Entra using existing identity and interactive browser modes.

Expected:

- clients follow the selected auth path and optional tenant;
- entity scope exposes no namespace-wide claims;
- save is always off for a new/edited profile unless explicitly enabled;
- Windows writes only Credential Manager, macOS only Keychain Services, and Linux only Secret
  Service/libsecret; no credential file appears;
- unavailable, locked, denied, provider-missing, unsupported, and not-found outcomes remain
  distinct, preserve profile/reference, and prompt for SAS;
- replacement and optional cleanup affect only the referenced native item and report partial
  failure honestly;
- persisted JSON contains only fields from `ConnectionProfile`, including at most the opaque
  reference;
- no SAS key, full connection string, Entra token, body, or properties appear in settings,
  history, logs, crash/support output, or test evidence.

Package-level vault smoke tests must run against the extracted self-contained artifact, not only
`dotnet test`, so missing native libraries, D-Bus/session behavior, entitlements, and P/Invoke
resolution are detected.

## Scenario 3: Messaging and Recovery

Exercise send, bounded peek, peek-lock receive, confirmed receive-and-delete, all settlements,
session acquisition/loss, deferred retrieval, and selected recovery.

Expected:

- every source-specific action names the source;
- peeked/expired/settled messages cannot settle;
- session loss disables further unsafe work;
- recovery sends replacement before changing an eligible original;
- partial outcomes distinguish confirmed successes and retryable failures.

## Scenario 4: Administration and Stale State

Create/update/delete queue, topic, subscription, and rules in an isolated namespace. Introduce an
external concurrent edit and a permission denial.

Expected:

- deletes require named-target confirmation;
- default catch-all behavior is explicit;
- accepted values refresh from Azure;
- conflict/permission failures do not claim success and mark or refresh stale state.

## Scenario 5: Accessibility

Complete connect, browse, send, receive, settlement, and confirmation using keyboard only on each
OS. Repeat P1 flows with an available screen reader.

Expected:

- no keyboard trap, visible logical focus, and predictable modal focus restoration;
- controls, tree/table state, source, message eligibility, validation, progress, errors, and
  confirmations have meaningful announced semantics;
- color is not the sole status/risk indicator.

## Scenario 6: Transactional DurationEditor

1. At application `MinWidth=820`, open each duration property and confirm the primary row shows
   `D.HH:MM:SS[.fff]` plus **Edit duration**.
2. Repeat at 100%, 150%, and 200% scaling with zero, non-zero milliseconds, greater than 365 days,
   and maximum `10675199.02:48:05.477`.
3. Type strict invariant text in the primary draft and use the Flyout's labelled Days/Hours/
   Minutes/Seconds/Milliseconds fields.
4. Traverse by keyboard, use Up/Down on each component, then test Apply, Cancel, Escape, and light
   dismiss.
5. Enter empty, malformed, negative, fractional, per-component out-of-range, and total-overflow
   values in multiple fields.
6. Enter a shared-range value outside a selected Azure property's allowed range.

Expected:

- no permanent spinner arrows, clipped labels/actions, overlap, or digits hidden behind adorners;
- strict Core parsing/formatting round-trips whole milliseconds across the full shared range;
- only valid Apply mutates the bound value exactly once;
- every non-Apply close preserves the exact original and returns focus to Edit;
- all invalid fields expose associated accessible errors;
- contextual validation names the property/limit without clamping the draft or shared range.

## Scenario 7: Send View Resolution and Guidance

Navigate to Send from queue, topic, and subscription detail without interacting with any duration
property. Review every field label and helper line with keyboard and assistive technology, then use
focused fakes to capture the path passed to the current backend. Attempt one send with an empty body
and one scheduled send.

Expected:

- App resolves `SendMessageViewModel` to the existing `SendMessageView`;
- queue uses its queue path and topic uses its topic path;
- subscription identifies and uses its parent topic path before send and in success/failure
  outcomes, never a direct subscription path;
- body and message count are visibly and programmatically required; session ID and schedule delay
  explain their conditional requirements; all other current properties remain optional;
- every current input states meaning, format or unit, and Azure effect, while deferred
  subject/label, partition, reply, absolute scheduling, and TTL fields are named as unavailable;
- empty body validation preserves the complete draft and starts no backend call;
- relative scheduling uses `DurationEditor` and preserves the current one-minute through seven-day
  range;
- no “view not found”/raw view-model display appears;
- destination resolution remains independent from duration editing.

## Scenario 8: Final Preview Packages and Coexistence

Produce and smoke-test:

- `win-x64.zip`
- `osx-x64.zip`
- `osx-arm64.zip`
- `linux-x64.tar.gz`

Each package must launch to the connection screen and expose version/preview status. Documentation
must state signing status, known exclusions, and Linux Secret Service prerequisites. Each package
must pass native vault store/retrieve/replace/delete and provider-failure smoke tests. On Windows,
separately build and launch the legacy application.

## Evidence

Record command output, package checksums, OS/runtime versions, test result summaries, and sanitized
screenshots/assistive-technology notes. Do not attach profiles, credentials, raw messages, or live
service exception payloads.

Normative contracts: [application services](contracts/application-services.md) and
[UI behavior](contracts/ui-behavior.md).
