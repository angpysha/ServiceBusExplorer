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
2. SAS requests current-attempt credentials and clearly states they will not be saved.
3. Entra offers **Use existing developer/environment identity** and **Interactive browser** plus
   an optional tenant/directory identifier.
4. Scope is explicit: **Namespace** or **Entity**. Entity requires entity kind/path and hides or
   disables namespace-only loading options.
5. Successful connection may save only the approved profile fields.
6. Reconnect from profile requests SAS again or reacquires Entra authorization.
7. History supports add/edit/remove and remains usable when one stored record is corrupt.

The connect command is disabled until required non-secret and transient inputs for the chosen mode
are present. Switching auth mode clears credential-only inputs from the previous mode.

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
