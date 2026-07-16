# Validation Quickstart: Safe Service Bus MVP

This guide defines runnable validation after implementation. It does not authorize production code
before tasks and human review.

## Prerequisites

- .NET 10 SDK
- Representative Azure Service Bus namespace with isolated test queues/topics/subscriptions
- SAS policy and Entra identity with intentionally varied permissions
- Session-enabled, dead-letter, transfer-dead-letter, and deferred test fixtures
- Windows 10 22H2+, macOS 13+, and Ubuntu 22.04+ validation hosts

Never place credentials or message content in source, test snapshots, command history, or logs.
Live tests acquire identity from the environment or receive SAS through a secret environment
variable.

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
- safety routing, confirmation, profile serialization, auth selection, settlement eligibility,
  session loss, recovery partial failure, accessibility semantics, and duration round-trip pass;
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

This is the required first implementation slice and maps to AC-4/AC-5 and SC-003/SC-004.

## Scenario 2: Secret-Free Connections

1. Connect with SAS, disconnect, and inspect history.
2. Restart and reconnect; enter SAS again.
3. Connect with Entra using existing identity and interactive browser modes.
4. Exercise entity scope and namespace scope.
5. Cancel and fail authentication once.

Expected:

- clients follow the selected auth path and optional tenant;
- entity scope exposes no namespace-wide claims;
- persisted JSON contains only fields from `ConnectionProfile`;
- no SAS key, full connection string, token, body, or properties appear in history or logs;
- cancellation and failure give distinct safe outcomes.

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

## Scenario 6: Packages and Coexistence

Produce and smoke-test:

- `win-x64.zip`
- `osx-x64.zip`
- `osx-arm64.zip`
- `linux-x64.tar.gz`

Each package must launch to the connection screen and expose version/preview status. Documentation
must state signing status and known exclusions. On Windows, separately build and launch the legacy
application.

## Evidence

Record command output, package checksums, OS/runtime versions, test result summaries, and sanitized
screenshots/assistive-technology notes. Do not attach profiles, credentials, raw messages, or live
service exception payloads.

Normative contracts: [application services](contracts/application-services.md) and
[UI behavior](contracts/ui-behavior.md).
