# Desktop UI Behavior Contract

**Parent**: [spec.md](../spec.md)  
**Surface**: .NET 10 Avalonia preview only

## Global Behavior

- Navigation MUST show only implemented Service Bus capabilities. Event Grid, Relay, Event Hubs,
  Notification Hubs, generators, and monitoring MUST be absent from preview navigation rather than
  visible no-op controls.
- Every asynchronous workflow exposes idle, loading, empty, completed, cancelled, stale,
  partial-success, and failed states where applicable.
- Starting work MUST NOT block the UI dispatcher. A visible cancel action is available while work
  can be safely interrupted.
- Errors identify operation, safe target context, category, and retry/correction path. Raw
  connection strings, tokens, message body, and custom properties are not displayed as diagnostics.

## Connection Experience

1. User selects **SAS** or **Microsoft Entra ID** before connecting.
2. SAS requests current-attempt credentials. **Save SAS in this device's credential vault** is
   off by default, requires explicit selection, names the platform store, and states that no
   plaintext or application-managed credential file is created.
3. Entra offers **Use existing developer/environment identity** and **Interactive browser** plus
   an optional tenant/directory identifier.
4. Scope is explicit: **Namespace** or **Entity**. Entity requires entity kind/path and hides or
   disables namespace-only loading options.
5. After a successful opted-in SAS connection, vault-save success permits the profile to store only
   an opaque reference. Vault-save failure reports that the connection succeeded but reconnect was
   not saved.
6. Reconnect resolves a saved SAS reference when available. Unavailable, locked, denied, missing
   provider, unsupported, or missing credential states preserve the profile and prompt for SAS.
7. Manual SAS entry after vault failure does not replace the saved credential unless the user
   explicitly chooses **Replace saved SAS**.
8. Reconnect for Entra reacquires authorization; this feature never offers to save an Entra token.
9. History supports add/edit/remove and remains usable when one stored record is corrupt.
10. Removing a profile with a reference explicitly asks whether to remove the native-vault item.
    Vault cleanup and profile removal outcomes are reported separately.

The connect command is disabled until required non-secret and transient inputs for the chosen mode
are present. Switching auth mode clears credential-only inputs from the previous mode.

Vault errors use safe, actionable copy:

- unavailable/locked: unlock or retry, or enter SAS for this connection;
- permission denied: review OS vault permission or enter SAS;
- provider missing/unsupported: install or enable a compatible Linux Secret Service where
  applicable, or enter SAS;
- credential not found: enter SAS and optionally replace the saved credential;
- cleanup failed: retry cleanup, keep the profile, or explicitly remove metadata only.

No error text contains the credential, a raw native exception, or a credential-derived label.

## Message Source Selection

- Message views present `Active`, `Dead-letter`, and `Transfer dead-letter` as explicit source
  choices only when supported.
- Initial selection is **none**. The source label remains visible beside message results and every
  source-specific action.
- Peek may require selection but is non-destructive. Receive, purge, and recovery MUST remain
  disabled until selection.
- Empty/unavailable source displays its own state; it never changes selection or loads active
  messages automatically.

## Destructive Confirmation

Confirmation is modal and contains:

- action title;
- named entity/rule/destination;
- source for source-specific actions;
- concise consequence and irreversibility/loss warning;
- known item count when relevant;
- a safe default focused on **Cancel**;
- distinct **Cancel** and action-specific confirm labels.

Escape/cancel/window-close returns `Cancelled`. Enter MUST NOT accidentally confirm unless focus is
already on the confirm action. Focus returns to the invoker after close. No mutation begins before
`Confirmed`.

Receive-and-delete explicitly warns that a message can be lost if display fails after receipt.
Recovery identifies destination and diagnostic-property treatment. Rule changes identify catch-all
delivery consequences.

## Message and Session State

- Peeked and locked messages are visually and programmatically distinguishable.
- Settlement actions are available only for a currently eligible locked message.
- Terminal settlement removes or updates the item once; the same action cannot be invoked twice.
- Session ID, acquisition state, and lock state are visible before session message actions.
- Session loss disables unsafe actions and offers reacquire/cancel without silently changing
  session.
- Binary, malformed, empty, and truncated bodies preserve metadata and announce representation.
  Copy/export is deliberate and warns that content may be sensitive.

## Keyboard Contract

- Tab order follows visual/task order; no keyboard trap exists.
- All required actions are reachable without pointer input.
- Source selectors, trees, tables, tabs, forms, message actions, confirmations, and cancellation
  expose standard keyboard operation.
- Destructive shortcuts never bypass confirmation.
- Focus is visible with platform theme/high-contrast support and is moved predictably when pages,
  errors, and modal dialogs open or close.

## Assistive Technology Contract

Avalonia automation metadata or equivalent semantics MUST expose:

- unique accessible names for icon-only controls;
- names, roles, current values, and validation relationships for inputs;
- hierarchy, expand/collapse, selection, and item counts for entity trees/tables;
- source, receive kind, settlement eligibility, truncation, and session state for messages;
- polite live announcements for loading/progress and assertive announcements for actionable errors;
- dialog title, target, source, consequence, and default/cancel actions.

Color is never the only indicator of source, risk, status, selection, or failure.

## Duration Contract

Duration editing exposes days, hours, minutes, seconds, and milliseconds. Values greater than 365
days and millisecond precision round-trip without a product cap. Service-invalid values produce
field-level validation while preserving the entered value.

## Preview Package Contract

Each artifact identifies product version, preview status, OS, architecture, signing/notarization
status, extraction/launch steps, and known limitations. Windows documentation names the legacy
application as a separate fallback and does not imply that the preview replaced it.
