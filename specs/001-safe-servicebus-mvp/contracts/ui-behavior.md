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

### First internal version

This subsection takes precedence until the native-vault milestone:

1. History lists only non-secret profile labels/endpoints/auth/scope metadata.
2. Selecting a SAS profile restores metadata but leaves the full SAS field empty, focuses or
   exposes it predictably, and states **SAS is not saved in this internal build**.
3. Every SAS connection, including repeat and post-restart use, requires full re-entry.
4. No **Save SAS**, **Replace saved SAS**, vault status, or credential-reference UI is present.
5. Legacy raw-string history is never shown. If startup cannot sanitize/remove it, connection UI is
   blocked behind a safe actionable error rather than continuing with plaintext persistence.
6. Title/About/status identifies **Internal development build**, revision, incomplete parity, and
   the absence of saved-SAS reconnect. Internal wording never implies a secret-storage exception.

### Native-vault MVP behavior

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

## Send Experience

- Queue, topic, and subscription Send surfaces resolve the same existing `SendMessageView`.
- Before submission, queue text names the queue; topic text names the topic; subscription text
  states **Publishes to parent topic: {topic}** while retaining the subscription name as context.
- Success and failure outcomes name the actual queue/topic destination. No subscription wording
  says or implies that Service Bus sends directly to a subscription.
- A failed validation or backend attempt leaves the draft available.
- The first internal version uses the current send backend path. View availability and destination
  truthfulness do not claim broader messaging completeness.

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

### Primary surface

- The control is named `DurationEditor`; the modern `TimeSpanControl` name and implementation are
  retired. No WinForms popup, message-loop, WndProc, or P/Invoke code is reused.
- One form row contains a compact invariant text field and adjacent **Edit duration** button.
- Closed-state text is Core-formatted `D.HH:MM:SS[.fff]`. The primary field accepts direct invariant
  typing as a draft; typing begins the same transaction and exposes the structured editor so Apply
  remains the only commit action.
- The field exposes the current committed value, format help, and invalid-text error. It never
  two-way binds raw text directly to the bound value.

### Structured editor

- Edit opens an Avalonia Flyout/Popup anchored to the button and snapshots the exact bound value.
- Focus enters Days, then Hours, Minutes, Seconds, Milliseconds, field errors, Apply, and Cancel in
  logical visual order.
- Every component has a persistent full `Label` associated with its input; abbreviations and
  placeholders do not replace labels.
- Direct typing is primary. Keyboard Up/Down increments the focused valid component within its
  component range. App MAY use `NumericUpDown` with `ShowButtonSpinner=False` and
  `AllowSpin=True`; permanently visible arrow buttons are prohibited.
- Days permits all values that can compose within the shared range; Hours is 0–23, Minutes and
  Seconds 0–59, and Milliseconds 0–999. Inputs retain enough width/scroll behavior that digits
  remain inspectable and editable.

### Transaction and validation

- Draft text/components are isolated from the bound `DurationValue`.
- Apply validates all fields, composition/overflow, and the supplied Azure-property constraint.
  A valid candidate commits exactly once, closes, and returns focus to Edit.
- Empty, malformed, negative, fractional, component-range, or composed-overflow failures identify
  every affected field and keep the editor open with bound value unchanged.
- A contextual service-limit failure names the Azure property and accepted limit, retains the
  representable draft, and leaves the bound value unchanged. It never clamps or changes the
  editor's shared range.
- Cancel, Escape, light dismiss, window deactivation/closure, or any non-Apply close discards the
  entire draft, leaves the exact original bound value unchanged, and returns focus to Edit when the
  owning window remains active.

### Responsive and accessible behavior

- Automated layout matrices render at the current app minimum width of 820 device-independent
  pixels and scale factors 1.0, 1.5, and 2.0.
- The primary row, maximum canonical value `10675199.02:48:05.477`, all five labels/inputs, errors,
  and Apply/Cancel remain available with no clipping, overlap, or digit hidden by adorners. The
  Flyout MAY reflow vertically and constrain itself to the work area.
- Automation semantics expose control name, committed value, invariant-format help, Edit action,
  each label/current draft value, validation relationship, and Apply/Cancel purpose. Validation is
  announced without moving focus unexpectedly.

### Independent send-view defect

`SendMessageViewModel` resolving to no view is not a duration-control failure. App registers its
existing `SendMessageView` DataTemplate and verifies template resolution in the send-message slice;
DurationEditor tests neither own nor mask that repair.

## Numeric Input Contract

- `DurationEditor` is the universal reusable editor for duration-valued properties that use the
  product's whole-millisecond `DurationValue` model. A named or parameterized
  `DurationConstraint` supplies property-specific validation without changing the shared range.
- Counts and sizes remain true whole-number inputs. They use a shared Avalonia numeric style with
  visible spinner controls, a minimum width sufficient for the configured maximum, integer
  formatting, direct typing, and explicit field-level minimum, maximum, and increment.
- Every numeric input has a persistent visible label plus a programmatic name and help description
  that states its unit or effect. Repeated toolbar inputs such as peek counts retain their labels.
- At 820 DIPs and 100%, 150%, and 200% scaling, the complete configured maximum and both spinner
  buttons remain inside the input without overlapping or obscuring digits.
- Add/remove buttons are actions, not numeric steppers. A symbol-only action exposes a descriptive
  automation name and help text and is excluded from numeric range behavior.

## Preview Package Contract

Each artifact identifies product version, preview status, OS, architecture, signing/notarization
status, extraction/launch steps, and known limitations. Windows documentation names the legacy
application as a separate fallback and does not imply that the preview replaced it.

The first internal artifact is a separate pre-preview gate. It MAY use `dotnet run` or a
single-host development publish and MUST identify itself as an internal development build with
revision and limitations. It MUST NOT claim final RID coverage, signing, native-vault package
validation, or preview readiness.
