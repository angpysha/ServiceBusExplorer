# Desktop Application Service Contracts

**Parent**: [spec.md](../spec.md)  
**Applies to**: Core ports implemented by Services/App; this is not an HTTP API.

Normative keywords MUST, MUST NOT, SHOULD, and MAY have their usual requirement meanings.

## Connection Contracts

### `IConnectionContextFactory`

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

### `IConnectionProfileStore`

- `ListAsync`, `UpsertAsync`, and `RemoveAsync` operate only on `ConnectionProfile`.
- Serialization MUST be allowlist-based and versioned.
- A corrupt/unreadable store returns a recoverable safe outcome and MUST NOT block a fresh
  connection.
- Existing raw-string history MUST NOT be rewritten or echoed. Migration requires explicit user
  review and retains only approved metadata.

## Message Operation Contracts

Every method below requires an explicit `MessageSource`; no overload or optional parameter may
default to active.

### `IMessageBrowseService`

- `PeekAsync(EntityAddress, MessageSource, PageRequest, CancellationToken)` is non-destructive,
  bounded, and returns source-tagged observed messages plus continuation information.
- Empty or unavailable source returns an empty/unavailable result for that source only.

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
