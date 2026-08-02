# Desktop Application Service Contracts

**Parent**: [spec.md](../spec.md)  
**Applies to**: Core ports implemented by Services/App; this is not an HTTP API.

Normative keywords MUST, MUST NOT, SHOULD, and MAY have their usual requirement meanings.

## Connection Contracts

### `IConnectionContextFactory`

Post-internal target contract; it is not required to replace the current bootstrap path before the
first internal candidate.

`CreateAsync(ConnectionRequest request, CancellationToken cancellationToken)`:

1. MUST validate endpoint, authentication, scope, entity path, and selected loading options before
   creating clients.
2. MUST construct clients from either the transient SAS connection string or fully qualified
   namespace plus the selected `TokenCredential`.
3. MUST NOT persist, log, return in outcomes, or copy transient credentials into a profile.
4. MUST honor cancellation during identity acquisition and capability probes.
5. MUST return one async-disposable context owning its client lifetime.
6. MUST omit namespace administration services for entity-scoped connections.
7. MUST classify validation, authentication, authorization, cancellation, throttling, and service
   availability failures without exposing raw secrets.
8. After the native-vault milestone, for a profile with a credential reference, MUST retrieve through `ICredentialVault`; any
   non-success result preserves the profile/reference and requests transient SAS input.

### `IConnectionProfileStore`

- `ListAsync`, `UpsertAsync`, and `RemoveAsync` operate only on `ConnectionProfile`.
- Serialization MUST be allowlist-based and versioned.
- A corrupt/unreadable store returns a recoverable safe outcome and MUST NOT block a fresh
  connection.
- Existing raw-string history MUST NOT be rendered, echoed, logged, copied to a new store, or used
  for reconnect. Before first-internal use, migration MUST atomically overwrite it with verified
  approved metadata or remove it; inability to eliminate persisted raw values blocks startup/use
  of the internal candidate.
- First-internal serialization has no credential-related member, including no null/empty
  credential reference. Selecting a SAS profile MUST require fresh full SAS input.
- Only after the native-vault milestone may the schema add an optional opaque random reference.
  New references MUST be CSPRNG-generated and contain no credential-derived or profile-derived data.

### `ICredentialVault`

Framework-neutral asynchronous port owned by Core and implemented only behind the
App/infrastructure boundary:

This port and all save/retrieve UI are absent from first-internal production composition. They are
enabled only by the later native-vault/connection gate.

- `GetAvailabilityAsync(CancellationToken)` returns a typed availability result.
- `StoreAsync(CredentialReference reference, SensitiveCredential credential, CancellationToken)`
  creates or explicitly replaces the referenced SAS secret.
- `RetrieveAsync(CredentialReference reference, CancellationToken)` returns a typed result and a
  transient non-serializable `SensitiveCredential` only on success.
- `DeleteAsync(CredentialReference reference, CancellationToken)` deletes only the referenced item.

All operations MUST distinguish `Available`, `Unavailable`, `Locked`, `PermissionDenied`,
`ProviderMissing`, `Unsupported`, `NotFound`, `Cancelled`, and `Failure` where applicable. Results
MUST also permit `Uncertain` when a native mutation outcome cannot be proven. Results contain safe
category/recovery information only, never native handles, native API types, raw native exceptions,
secret values, or secret-derived identifiers.

Implementation contract:

- Windows uses current-user Windows Credential Manager generic credentials.
- macOS uses the current user's Keychain Services generic-password item.
- Linux uses the current user's freedesktop Secret Service through libsecret or a compatible
  provider and MUST report provider absence rather than falling back.
- Production composition MUST NOT register an in-memory, plaintext, DPAPI/file, encrypted-file, or
  other application-managed persistence fallback.
- Core and ViewModels MUST NOT reference P/Invoke, COM, D-Bus, GLib, Security.framework,
  platform-specific packages, or third-party vault APIs.
- Microsoft Entra access/refresh tokens MUST NOT be passed to this port.

Lifecycle and ordering contract:

1. The save toggle starts false for every new profile and is never inherited.
2. After a successful SAS connection, explicit save generates a random reference, stores the secret,
   and only then persists the reference.
3. Store failure leaves the profile without a new reference and reports “connected, reconnect not
   saved.”
4. Explicit replacement upserts the existing reference. Failure/uncertainty keeps the profile
   reference and MUST NOT claim replacement or claim that the previous value remains; retry/manual
   entry remains available.
5. Profile deletion asks separately whether to delete the referenced vault item.
6. If cleanup was requested, vault deletion is attempted first. Failure retains the profile and
   reference for retry unless the user explicitly chooses metadata-only deletion after warning.
7. Retrieval `NotFound` or any unavailable/locked/denied/provider failure leaves the profile and
   reference unchanged and prompts for SAS.

Package implementations remain subordinate to this contract. No package may be adopted until
license, maintenance, transitive dependency, native interop, fallback, supply-chain, and packaged
three-platform smoke review passes.

## Message Operation Contracts

Every method below requires an explicit `MessageSource`; no overload or optional parameter may
default to active.

### `IMessageBrowseService`

- `PeekAsync(EntityAddress, MessageSource, PageRequest, CancellationToken)` is non-destructive,
  bounded, and returns source-tagged observed messages plus continuation information.
- Empty or unavailable source returns an empty/unavailable result for that source only.

### Current-path Send Contract

The first internal milestone MUST preserve the existing
`IQueueService.SendAsync(string entityPath, OutboundMessage message)` backend path while making its
actual destination explicit:

- queue context supplies the queue path;
- topic context supplies the topic path;
- subscription context supplies the parent topic path, never
  `topic/Subscriptions/subscription`;
- `SendTargetContext` MUST be constructed explicitly and expose requested context plus actual
  destination before submission;
- success, validation failure, authorization failure, and service failure outcomes MUST name the
  actual queue/topic destination. Subscription outcomes MUST state that the parent topic was the
  publish target;
- draft validation failure or send failure MUST preserve the draft;
- no parallel send service, broad connection refactor, or native-vault dependency is required for
  this internal slice.

### `IMessageReceiveService`

- `OpenPeekLockAsync(EntityAddress, MessageSource, SessionRequest?, CancellationToken)` returns an
  async-disposable receive/session handle.
- `ReceiveAndDeleteAsync` requires a previously confirmed operation token/request supplied by the
  application orchestration layer; it reports display-loss risk.
- A receive handle MUST reject settlement of peeked, expired, lost, or terminal messages.
- `CompleteAsync`, `AbandonAsync`, `DeferAsync`, and `DeadLetterAsync` are single-attempt per
  currently eligible lock and return typed outcomes.

### `IPurgeService`

`PurgeAsync(EntityAddress target, MessageSource source, CancellationToken cancellationToken)`:

- MUST receive a non-null explicit source.
- MUST map `Active`, `DeadLetter`, and `TransferDeadLetter` exhaustively to Azure receiver options.
- MUST use receive-and-delete only after confirmation has completed outside this adapter.
- MUST be bounded per receive, cancellable between batches, and report confirmed count plus any
  uncertain remainder.
- MUST NOT retry the whole operation automatically after partial progress.

### `IRecoveryService`

`RecoverAsync(RecoveryRequest, CancellationToken)`:

- validates explicit source, selected message identities, compatible destination, and dead-letter
  property treatment;
- sends each replacement before settling an eligible original;
- never claims a peeked dead-letter original was removed;
- returns per-item `Succeeded`, `Failed`, `Cancelled`, or `Uncertain`;
- excludes confirmed successes from a retry request.

## Confirmation Contract

### `IConfirmationService`

`ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken)` returns
`Confirmed` or `Cancelled`.

- Request MUST include operation, named target, consequence, and source for source-specific work.
- ViewModels MUST call it immediately before the service-changing operation.
- `Cancelled` or cancellation MUST result in zero calls to the destructive service port.
- Core/ViewModels MUST NOT reference Avalonia `Window`, control, dispatcher, or dialog classes.
- App presentation MUST restore focus to the invoking control and expose title, target, source,
  consequence, confirm, and cancel semantics to assistive technology.

## Administration Contracts

Queue, topic, subscription, and rule create/update/delete contracts:

- accept only service-supported fields;
- carry last-observed version/etag when available;
- require confirmation before delete;
- refresh authoritative state after success;
- return conflict/stale rather than overwrite a newer service value;
- represent default catch-all rule behavior explicitly.

## Duration Value Contract

Core MUST expose a framework-neutral immutable `DurationValue` and validation functions with no
Avalonia, WinForms, culture, popup, or control dependency.

- Shared range is total whole milliseconds `0..922337203685477`.
- `FormatInvariant()` returns exactly `D.HH:MM:SS[.fff]`; milliseconds are omitted only when zero.
- Strict parse accepts only that invariant grammar and returns typed field/format/overflow errors.
- Component composition accepts non-negative whole Days, Hours 0–23, Minutes/Seconds 0–59, and
  Milliseconds 0–999, then checks composed-total overflow.
- Conversion to/from `TimeSpan` rejects negatives and non-zero sub-millisecond ticks rather than
  silently rounding.
- `DurationConstraint` validates a candidate for a named Azure property independently of shared
  representability. Context failure leaves the candidate and bound value unchanged and identifies
  property plus accepted limit.
- Services map `DurationValue` to Azure SDK `TimeSpan` only at the adapter boundary.

## App View Resolution Contract

App composition MUST register `SendMessageViewModel -> SendMessageView` as an explicit
`DataTemplate`. This is a send-page defect and MUST be tested/fixed in the send-message slice,
independently of `DurationEditor`. Duration control implementation or registration MUST NOT be used
as an implicit workaround for missing view resolution.

The same template MUST resolve in queue, topic, and subscription contexts. Context-specific
destination text comes from `SendTargetContext`, not from separate duplicated views.

The first-internal `SendMessageViewModel` treats non-whitespace body content as required and returns
a target-specific validation error before any service call when it is absent. The complete draft
remains unchanged. Content type, message ID, correlation ID, session ID, `To`, and application
properties retain their current optional backend mapping; presentation guidance may describe
session ID as conditionally required by a session-enabled destination without making it globally
required.

## Operation and Diagnostic Contract

All network ports:

- are asynchronous and accept `CancellationToken`;
- return or throw only through an application translator that produces an `OperationOutcome`;
- use Azure SDK retry for transient transport failures but do not retry acknowledged destructive
  items at orchestration level;
- log operation name, target category, source, elapsed time, and safe category only;
- MUST NOT log connection strings, SAS keys, tokens, message bodies, application properties, or
  raw profile persistence payloads.

## Contract Verification

Fake-backed contract tests MUST prove:

1. null/unselected source cannot reach a message port;
2. each source maps to its exact Azure sub-queue and never another;
3. cancellation before confirmation or operation causes no mutation;
4. successful confirmation causes exactly one requested mutation;
5. profile serialization has no credential/content fields;
6. SAS and each Entra selection create the expected client path and honor tenant/scope;
7. context disposal disposes clients once after in-flight work is cancelled or completed;
8. partial recovery does not repeat confirmed successes.
9. SAS saving defaults off and no vault call occurs without explicit opt-in;
10. profile JSON contains only an opaque reference after successful vault store;
11. all vault failure classes preserve the profile/reference and prompt for SAS;
12. replacement and cleanup failures/uncertainty preserve the profile/reference and never claim an
    unproven stored/deleted state;
13. native adapters use only the designated platform store and create no fallback file;
14. Entra token paths never invoke `ICredentialVault`.
15. duration parsing/formatting round-trips every boundary value using invariant grammar;
16. component and composed overflow errors never create or mutate a bound value;
17. contextual Azure property validation never narrows or clamps `DurationValue`;
18. App template resolution returns `SendMessageView` for `SendMessageViewModel` without loading or
    depending on `DurationEditor`.
19. first-internal settings serialize no connection string, SAS fragment/key, token, credential-
    derived value, or credential-reference property after successful, failed, cancelled, repeated,
    migrated, and restarted connection scenarios;
20. selecting any first-internal SAS profile requests a full transient SAS value and makes zero
    `ICredentialVault` calls;
21. queue, topic, and subscription send tests capture respectively queue path, topic path, and
    parent topic path, and subscription UI/outcomes never claim direct subscription send;
22. internal startup refuses use when legacy raw history cannot be sanitized or removed.
23. empty body validation starts no service call and preserves all current draft fields, while
    optional send properties retain their existing mappings.
