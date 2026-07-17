# Feature Specification: Safe Service Bus MVP

**Feature Branch**: `feature/avalonia-servicebus-mvp`

**Created**: 2026-07-16

**Status**: Approved requirements — amended 2026-07-17 for optional native-vault SAS persistence and compact duration editing

**Input**: Deliver a minimum working cross-platform Service Bus Explorer centered on safe Azure Service Bus administration and message recovery while retaining the legacy application during preview.

## Clarifications

### Session 2026-07-17

- Q: May users persist a full SAS connection string for reconnect? → A: Yes, only through an explicit option that is disabled by default and stores the string in the operating system's native credential vault; history retains only an opaque random reference, and Microsoft Entra access tokens are never persisted.
- Q: Which duration-entry experience replaces the arrow-heavy TimeSpanControl? → A: A compact invariant-format duration text field with an adjacent Edit affordance that opens a fully labeled structured editor popover; values remain visible, keyboard and assistive-technology behavior is explicit, and service-specific limits are contextual validation rather than editor range limits.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Connect Safely and Browse a Namespace (Priority: P1)

As a developer or operator, I can connect to an Azure Service Bus namespace using either a shared access signature (SAS) or Microsoft Entra ID, optionally save a SAS connection string in my operating system's native credential vault, reconnect from non-secret history, and browse the resources I am allowed to see without exposing credentials.

**Why this priority**: Every useful workflow depends on a trustworthy connection. Preventing plaintext, unintended, or application-managed credential persistence is a release-blocking safety requirement.

**Independent Test**: Connect once with each supported authentication method; verify that SAS saving is off by default; opt in to save one valid SAS connection string; close and reopen the application; verify that history contains only non-secret metadata and an opaque reference; reconnect through the native vault; then make the vault unavailable and verify that the profile remains while the application requests the SAS string again.

**Acceptance Scenarios**:

1. **Given** valid SAS connection details and the save option left at its default, **When** the user connects, **Then** the namespace is opened and the SAS connection string is not persisted.
2. **Given** a Microsoft Entra identity with access, **When** the user connects and completes any required sign-in, **Then** the namespace is opened without requesting a SAS secret.
3. **Given** a user explicitly enables SAS saving, **When** the connection succeeds, **Then** the full SAS connection string is stored only in the platform-native credential vault and history stores only non-secret profile metadata plus an opaque random credential reference.
4. **Given** invalid, expired, unauthorized, or cancelled authentication, **When** connection fails, **Then** the user receives an actionable, secret-safe explanation and can retry, edit the connection, or cancel.
5. **Given** a connection limited to one entity, **When** it opens, **Then** the application honors that scope and does not imply access to the whole namespace.
6. **Given** a profile that references a saved SAS credential, **When** its native vault is unavailable, locked, denied, or no longer contains that credential, **Then** the non-secret profile remains and the user is prompted to enter the SAS connection string again without any plaintext or application-managed file fallback.
7. **Given** a saved SAS credential, **When** the user updates it, **Then** subsequent reconnect uses the updated vault value and history still contains no SAS secret.
8. **Given** a profile with a saved SAS credential, **When** the user removes the profile, **Then** the user is explicitly offered deletion of the corresponding native-vault entry.
9. **Given** any Microsoft Entra sign-in outcome, **When** the connection ends or the application restarts, **Then** no Microsoft Entra access token has been persisted by this feature.

---

### User Story 2 - Inspect and Safely Operate on Messages (Priority: P1)

As an operator, I can send, peek, receive, and settle messages on queues and subscriptions, with the selected message source always explicit and destructive actions guarded.

**Why this priority**: Message troubleshooting is the core user value, and ambiguous active/dead-letter routing can cause irreversible data loss.

**Independent Test**: Use a queue and a topic subscription containing active and dead-letter messages; send and inspect messages, receive in both supported modes, apply each settlement, and verify that every action affects only the displayed source.

**Acceptance Scenarios**:

1. **Given** a selected queue or topic destination, **When** the user sends a valid message, **Then** the message is accepted and the outcome identifies the destination without revealing sensitive content in diagnostics.
2. **Given** any supported message source, **When** the user peeks, **Then** messages are displayed without removing or locking them and the source is visibly identified.
3. **Given** an explicitly selected active, dead-letter, or transfer dead-letter source, **When** the user receives, **Then** messages come only from that source.
4. **Given** a peek-locked message, **When** the user completes, abandons, defers, or dead-letters it, **Then** the resulting state is shown and the same settlement cannot be applied twice.
5. **Given** a receive-and-delete operation, purge, entity deletion, or other irreversible action, **When** the user initiates it, **Then** a confirmation identifies the entity, source, and consequence before execution.
6. **Given** the dead-letter source is displayed, **When** the user chooses purge, **Then** only that dead-letter source is purged; active messages remain untouched.
7. **Given** no source has been explicitly selected, **When** the user attempts a source-specific destructive action, **Then** the action is blocked rather than defaulting to active messages.

---

### User Story 3 - Administer Service Bus Entities (Priority: P2)

As an administrator, I can view, create, update, and delete queues, topics, subscriptions, and subscription rules within my granted permissions.

**Why this priority**: Entity administration makes the preview application operationally useful beyond message inspection while remaining focused on Service Bus.

**Independent Test**: In an isolated namespace, create each entity type with representative settings, update supported mutable settings, add and change rules, then delete the created resources while verifying confirmation and refresh behavior.

**Acceptance Scenarios**:

1. **Given** sufficient permission, **When** the user creates a queue, topic, or subscription with valid values, **Then** it appears in navigation with its effective settings.
2. **Given** an existing entity, **When** the user changes a supported mutable setting, **Then** the refreshed detail shows the accepted value without changing unrelated settings.
3. **Given** a subscription, **When** the user creates, edits, or deletes a rule, **Then** the rule list reflects the service state and rule behavior is represented accurately.
4. **Given** a destructive administration action, **When** the user has not confirmed the named target, **Then** no deletion occurs.
5. **Given** insufficient permission, a conflict, invalid values, or a service limit, **When** administration fails, **Then** the user sees a recoverable explanation and the displayed state is refreshed or clearly marked stale.

---

### User Story 4 - Work with Sessions and Recover Messages (Priority: P2)

As an operator, I can work with session-enabled entities and complete common recovery workflows for dead-lettered or deferred messages without losing routing context.

**Why this priority**: Sessions and recovery are common production troubleshooting needs and are especially sensitive to ordering, ownership, and destination mistakes.

**Independent Test**: Use session-enabled and non-session entities with dead-lettered and deferred messages; accept a session, process messages in order, recover selected messages to a chosen compatible destination, and verify partial-failure reporting.

**Acceptance Scenarios**:

1. **Given** a session-enabled source, **When** the user selects a session identifier or requests the next available session, **Then** the accepted session is visible and only its messages are processed.
2. **Given** a session lock that expires or is lost, **When** the user attempts another operation, **Then** the application stops unsafe continuation and offers a clear retry path.
3. **Given** selected dead-letter messages, **When** the user chooses recovery, **Then** the proposed destination and treatment of diagnostic dead-letter properties are shown before resubmission.
4. **Given** a deferred message and its sequence number, **When** the user requests recovery, **Then** the message can be retrieved and settled or resubmitted subject to current authorization and lock state.
5. **Given** a batch recovery where only some messages succeed, **When** processing finishes, **Then** successful and failed items are distinguished and failed items remain available for safe retry.

---

### User Story 5 - Run an Accessible Cross-Platform Preview (Priority: P3)

As a user on Windows, macOS, or Linux, I can install and operate the preview with keyboard and assistive technology, while retaining access to the legacy Windows application for workflows not yet migrated.

**Why this priority**: Cross-platform availability defines the migration's value, while preview coexistence prevents the MVP's deliberate scope limits from blocking existing users.

**Independent Test**: Install a preview package on each supported operating system, complete the P1 workflows using keyboard-only navigation and an available screen reader, and verify that the legacy Windows application remains separately launchable.

**Acceptance Scenarios**:

1. **Given** a supported operating system, **When** the user installs or extracts its documented preview package, **Then** the application launches and its version and preview status are identifiable.
2. **Given** keyboard-only use, **When** the user completes connect, navigation, send, receive, and confirmation workflows, **Then** focus order, focus visibility, labels, and shortcuts make every required action operable.
3. **Given** a screen reader, **When** the user navigates forms, entity trees, tables, message details, duration inputs, errors, and confirmations, **Then** meaningful names, roles, values, validation, and state changes are announced.
4. **Given** a duration value, **When** it is shown in the compact primary field, **Then** it uses the invariant format `D.HH:MM:SS[.fff]`, where days contain one or more digits, hours/minutes/seconds contain exactly two digits, and milliseconds appear as exactly three digits only when non-zero.
5. **Given** a Windows preview installation, **When** the user needs an excluded workflow, **Then** the legacy application remains available as a separate fallback and is not silently replaced.
6. **Given** focus on the adjacent Edit affordance, **When** the user activates it, **Then** a structured popover opens with persistent full labels for Days, Hours, Minutes, Seconds, and Milliseconds and supports direct numeric typing plus keyboard Up/Down increments.
7. **Given** edits in the duration popover, **When** the user selects Apply, **Then** the valid composed duration becomes the bound value and focus returns to the Edit affordance.
8. **Given** edits in the duration popover, **When** the user selects Cancel or presses Escape, **Then** the original bound value is restored unchanged, the popover closes, and focus returns to the Edit affordance.
9. **Given** one or more invalid duration components, **When** validation runs, **Then** each invalid field exposes its own error and the bound value remains unchanged.
10. **Given** the application at its documented minimum supported width and at 100%, 150%, or 200% display scaling, **When** any representable duration is displayed or edited, **Then** numeric values remain readable and are not clipped, obscured, or covered by increment controls.
11. **Given** a user of assistive technology, **When** focus moves through the compact duration field, Edit affordance, popover components, validation, and actions, **Then** each exposes its accessible name, current value, format or action help, and any field-specific error in logical order.
12. **Given** a duration that is representable by the product but outside the limit of the Azure property being edited, **When** the user applies it, **Then** contextual validation names the property and accepted limit without changing the duration or narrowing the general editor range.

### Edge Cases

- Empty, malformed, partially redacted, expired, revoked, or wrong-namespace SAS details are rejected without being persisted or echoed.
- Microsoft Entra sign-in may be cancelled, time out, require additional policy interaction, or succeed for an identity lacking data or administration permissions.
- Native credential storage is explicit and disabled by default; dismissing or declining the option leaves the SAS connection string available only to the current connection.
- The native credential vault may be unavailable, locked, denied, unsupported, or later cleared; the non-secret profile remains usable for manual credential entry and no plaintext or application-managed encrypted-file fallback is created.
- An opaque credential reference may be missing, malformed, duplicated through profile copying, or point to a deleted vault entry; it is never treated as a credential and failed resolution prompts for SAS again.
- Updating a saved SAS credential may fail after connection succeeds; the application reports that reconnect is not saved rather than claiming persistence.
- Profile removal and vault-entry removal may have different outcomes; the user chooses whether to delete the vault entry and receives the outcome without exposing the credential.
- A profile copied to another operating-system account or device does not carry its native-vault credential and therefore prompts for SAS.
- Namespace-wide browsing may be unavailable for an entity-scoped connection; unavailable operations remain disabled or explain the required scope.
- A connection may drop or credentials may expire during a long operation; completed work is distinguished from work that can be retried.
- An entity can be deleted, disabled, renamed externally, or changed between display and action; stale details must not be presented as confirmed success.
- Empty sources produce a clear empty result and never cause fallback to a different source.
- Active, dead-letter, transfer dead-letter, and deferred messages are distinct sources or states; an action never silently substitutes one for another.
- Transfer dead-letter may be unavailable for an entity or unsupported by its current routing; the application explains this without falling back.
- Peeked messages cannot be settled; settlement controls apply only to currently locked received messages.
- Locks may expire, be lost, or be settled by another consumer; the user is informed and unsafe repeat settlement is blocked.
- Receive-and-delete can lose a message if display fails after receipt; the warning and confirmation state that risk.
- Purge may be interrupted or throttled; results report confirmed removals and uncertainty rather than claiming all messages were removed.
- Message bodies may be empty, binary, malformed text, or larger than the display limit; metadata remains usable and truncation is explicit.
- Message properties may be unsupported, duplicated by case, invalid for sending, or too large; validation identifies the specific field.
- Scheduled, duplicate-detected, partitioned, session-enabled, auto-forwarded, and dead-lettered messages retain relevant routing and identity metadata where the service permits.
- Session identifiers may be empty where prohibited, unavailable, locked elsewhere, or associated with no messages.
- Rule creation must account for the default catch-all rule; the user is warned when a rule change would unintentionally broaden or stop delivery.
- Entity names, paths, filters, and values at service limits are validated; zero, negative, infinite, and sub-millisecond duration boundaries are handled explicitly.
- A compact duration may omit milliseconds only when they are zero; parsing and display never reinterpret a day component as hours or milliseconds as another unit.
- Empty, non-numeric, negative, fractional-component, or out-of-component-range duration input identifies the affected field and does not mutate the bound duration.
- A duration may be representable by the editor but invalid for a particular Azure property; the editor retains the value while contextual validation names that property and its accepted limit.
- Escape, Cancel, focus movement, validation failure, or popover dismissal cannot partially commit a duration; only Apply with a valid editor value commits.
- At the minimum supported window width or 100%, 150%, and 200% scaling, the primary value and popover fields reflow or resize without truncating unit labels or placing increment controls over numeric text.
- Extremely long day values remain scannable in the primary field and editable without a permanent row of spinner buttons; overflow is handled without hiding digits behind controls.
- Concurrent administration changes produce a conflict or refreshed state rather than silently overwriting newer values.
- Confirmation is required for deletion, purge, receive-and-delete, bulk settlement, and bulk recovery; cancellation leaves service state unchanged.
- A multi-item operation can partially succeed; per-item outcomes support safe retry without automatically repeating successful work.
- History may contain duplicate labels or namespaces under different identities; entries remain distinguishable and can be removed.
- Local history corruption or unreadability does not block a new connection and never causes secrets to be written as recovery data.
- Network failure, service throttling, transient outage, quota exhaustion, and permission denial provide distinct, actionable, secret-safe outcomes.
- Closing the application during active work asks the user to cancel or wait when interruption could leave outcomes uncertain.
- Very large entity and message lists remain cancellable and use bounded retrieval rather than implying a complete unbounded snapshot.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The product MUST provide a preview desktop experience on supported Windows, macOS, and Linux versions.
- **FR-002**: The product MUST keep the legacy Windows application available as a separate fallback throughout the MVP preview.
- **FR-003**: Users MUST be able to connect using SAS credentials or Microsoft Entra ID, and the selected authentication mode and optional organizational directory MUST be honored.
- **FR-004**: Connection inputs MUST distinguish namespace-wide and entity-scoped access, honor the supplied scope, and apply the user's selected entity-loading options.
- **FR-005**: Application settings and connection-history JSON MUST retain only non-secret profile data: a user label, namespace endpoint, authentication mode, optional organizational directory identifier, optional entity scope, non-sensitive display preferences, and, when SAS saving is enabled, an opaque random credential reference that contains no credential-derived data.
- **FR-006**: Saving a full SAS connection string MUST be an explicit option disabled by default and MUST store it only in the current user's platform-native credential vault: Windows Credential Manager on Windows, Keychain Services on macOS, and a freedesktop Secret Service provider through libsecret or a compatible provider on Linux. The product MUST NOT persist SAS secrets in history, settings, logs, crash or support diagnostics, plaintext files, or application-managed encrypted files, and MUST never persist Microsoft Entra access tokens through this feature.
- **FR-007**: Users MUST be able to add, edit, reconnect from, and remove profiles; save, update, or remove a profile's SAS credential in the native vault; and choose whether profile removal also deletes its referenced vault entry. Reconnect MUST use a referenced vault credential when available, reacquire Microsoft Entra authorization for Entra profiles, and prompt for SAS while retaining the profile when the vault or referenced credential is unavailable, locked, denied, or deleted.
- **FR-008**: Connection and operation failures MUST provide an actionable category, affected operation, and retry or correction path without exposing secrets.
- **FR-009**: Users MUST be able to browse and refresh the queues, topics, and subscriptions visible to their current connection and permissions.
- **FR-010**: Users with sufficient permission MUST be able to view, create, update, and delete queues, topics, and subscriptions, subject to service-supported mutability and limits.
- **FR-011**: Users with sufficient permission MUST be able to list, create, edit, and delete subscription rules, including an explicit representation of catch-all behavior.
- **FR-012**: Every destructive action MUST require confirmation that names the target and consequence; source-specific actions MUST also name the active, dead-letter, or transfer dead-letter source.
- **FR-013**: Source-specific destructive actions MUST be unavailable until the user explicitly selects or enters the intended source; no destructive command may infer the active source as a fallback.
- **FR-014**: Users MUST be able to compose and send a single message with body and commonly used routing, identity, scheduling, session, correlation, reply, content-type, time-to-live, and custom property values supported by the destination.
- **FR-015**: Send validation MUST identify invalid or conflicting message values before submission and preserve the user's draft after a failed attempt.
- **FR-016**: Users MUST be able to peek messages without locking or removing them from explicitly identified active, dead-letter, and transfer dead-letter sources where available.
- **FR-017**: Users MUST be able to receive messages in peek-lock mode and, after explicit risk confirmation, receive-and-delete mode.
- **FR-018**: For a currently locked message, users MUST be able to complete, abandon, defer, or dead-letter it, with an outcome recorded in the current activity view.
- **FR-019**: The product MUST prevent settlement of peek-only, expired-lock, already-settled, or otherwise ineligible messages.
- **FR-020**: Users MUST be able to purge messages from an explicitly identified source, with active, dead-letter, and transfer dead-letter purge treated as separate operations.
- **FR-021**: Users MUST be able to copy or export selected message body and metadata intentionally, with visible warnings that message content may be sensitive; automatic content logging is prohibited.
- **FR-022**: Message views MUST represent empty, text, structured text, binary, and truncated content without losing access to message metadata.
- **FR-023**: Users MUST be able to accept the next available session or request a specific session on session-enabled queues and subscriptions.
- **FR-024**: The current session identifier, lock state, and loss or expiry of session ownership MUST be visible before further message actions.
- **FR-025**: Users MUST be able to retrieve deferred messages by sequence number and perform valid settlement or recovery actions.
- **FR-026**: Users MUST be able to recover selected dead-letter or deferred messages by resubmitting them to an explicitly confirmed compatible destination.
- **FR-027**: Before recovery, users MUST choose whether dead-letter diagnostic properties are retained as ordinary custom properties or removed; the original message MUST remain untouched until the replacement send succeeds.
- **FR-028**: Bulk purge, settlement, and recovery MUST report per-item or reliable aggregate outcomes, distinguish partial success, and avoid automatically repeating confirmed successes.
- **FR-029**: All network and messaging operations MUST remain cancellable from the user's perspective, keep the interface responsive, and distinguish cancellation from failure.
- **FR-030**: The product MUST provide clear empty, loading, completed, cancelled, stale, partial-success, and failure states for connection, administration, and message workflows.
- **FR-031**: Required workflows MUST be operable by keyboard alone with visible focus and logical focus order.
- **FR-032**: Interactive controls, tables, trees, forms, message states, validation, progress, and confirmations MUST expose meaningful names, roles, values, and changes to assistive technology.
- **FR-033**: Duration values MUST display and preserve days, hours, minutes, seconds, and millisecond precision across view and edit operations, without an arbitrary product-imposed day cap or a smaller editor range than the product's shared duration value range.
- **FR-034**: Supported preview packages MUST identify operating system, architecture, version, preview status, installation or extraction steps, and known limitations.
- **FR-035**: Automated verification MUST cover safety-critical source routing, destructive confirmations, default-disabled SAS saving, native-vault-only SAS persistence and lifecycle behavior on each platform, absence of secrets and access tokens from history and settings, authentication option handling, message settlement eligibility, session loss, recovery partial failure, duration formatting and editing, the no-obscured-numeric-value regression, accessibility semantics, and package launch checks.
- **FR-036**: The primary duration field MUST fit on one form row and display an unambiguous invariant value in the format `D.HH:MM:SS[.fff]`: one or more day digits, exactly two digits each for hours, minutes, and seconds, and an optional exactly three-digit millisecond segment omitted only when zero.
- **FR-037**: An adjacent, accessible Edit affordance MUST open a structured duration popover whose Days, Hours, Minutes, Seconds, and Milliseconds labels remain fully visible; each component MUST support direct numeric typing and keyboard Up/Down increments without permanently visible spinner-arrow controls in the primary form. Days MUST be a non-negative whole number within the shared duration range, Hours MUST be 0–23, Minutes and Seconds MUST each be 0–59, and Milliseconds MUST be 0–999.
- **FR-038**: Duration interaction MUST have a logical focus order; expose accessible name, current value, format help, and field-specific error semantics; make Escape and Cancel discard all pending edits; make Apply the only commit action; and return focus to the Edit affordance after the popover closes.
- **FR-039**: Empty, malformed, negative, fractional-component, or out-of-range component input MUST identify every affected field and MUST NOT mutate the bound duration; Cancel or Escape MUST restore the exact original value after any valid or invalid pending edits.
- **FR-040**: The duration editor's representable range MUST match the product's shared non-negative duration value range independently of any Azure property limit. Service-specific minimums, maximums, and special constraints MUST be presented as contextual validation naming the affected property, without narrowing the editor's general range or silently changing the value.
- **FR-041**: At the application's documented minimum supported width and at 100%, 150%, and 200% display scaling, the primary duration value, full popover labels, numeric components, validation text, and actions MUST remain available without clipping, overlap, or digits disappearing behind increment controls; the layout MAY reflow to satisfy this requirement.

### Key Entities

- **Connection Profile**: A non-secret history entry containing a label, namespace endpoint, SAS or Microsoft Entra authentication mode, optional organizational directory identifier, optional entity scope, non-sensitive view preferences, and an optional opaque random credential reference.
- **Saved SAS Credential**: A full SAS connection string stored only in the current user's native operating-system credential vault and addressable through an opaque random reference; it is never embedded in the profile.
- **Live Connection**: The current authorized interaction with a namespace or entity, including granted scope, status, and cancellation state; credentials are not embedded in persisted history or settings.
- **Messaging Entity**: A queue, topic, or subscription with identity, status, counts, routing relationships, and service-supported settings.
- **Subscription Rule**: A named filter and optional action associated with a subscription, including whether it provides catch-all behavior.
- **Message Source**: The explicitly selected active, dead-letter, or transfer dead-letter location from which messages are inspected or consumed.
- **Message Draft**: User-entered body, system properties, custom properties, destination, and scheduling values prepared for sending.
- **Observed Message**: Message body representation and metadata obtained by peek or receive, including source, sequence number, lock and session state, and settlement eligibility.
- **Duration Value**: A product-wide non-negative value composed of days, hours, minutes, seconds, and milliseconds, with documented minimum and maximum endpoints independent of the narrower limits that individual Azure properties may impose.
- **Session**: Ordered message ownership context identified by a session identifier and time-bound lock.
- **Recovery Operation**: A user-confirmed attempt to retrieve and/or resubmit selected messages, with source, destination, property treatment, and per-item outcomes.
- **Operation Outcome**: Completed, cancelled, stale, partial, or failed result containing actionable non-secret context.

## Scope Boundaries and Decisions

### Included

- Azure Service Bus queues, topics, subscriptions, and subscription rules.
- SAS and Microsoft Entra ID connections, including entity-scoped connections.
- Non-secret connection history, optional native-vault SAS saving, and safe reconnect behavior.
- Send, peek, peek-lock receive, receive-and-delete, settlement, purge, sessions, deferred-message retrieval, and selected-message recovery.
- Explicit active, dead-letter, and transfer dead-letter routing.
- Automated safety, workflow, accessibility, and packaging verification.
- Preview distribution for Windows, macOS, and Linux with legacy Windows coexistence.

### Explicitly Excluded from the First MVP

- Event Grid and Relay workflows.
- Advanced Notification Hubs workflows.
- Broad Event Hubs exploration or administration.
- Performance generators, load-testing tools, throughput charts, and monitoring dashboards.
- Full parity with legacy configuration or message import/export formats.
- Automated Azure resource provisioning.
- Mobile and browser-hosted applications.
- Automatic migration or deletion of legacy saved connections.
- Automatic replay of every message in a source; recovery is deliberate and selection-based.

### Functional Options Chosen and Rejected

- **Connection history and SAS persistence**: Chosen — persist only non-secret profile metadata plus an optional opaque random reference; offer default-disabled SAS saving exclusively in Windows Credential Manager, macOS Keychain Services, or a Linux freedesktop Secret Service provider; use the vault value when available and otherwise prompt again while keeping the profile. Rejected — plaintext persistence; application-managed encrypted-file storage; fallback secret files when the vault is unavailable; mandatory or default-enabled saving; persisting Microsoft Entra access tokens; no history at all.
- **Destructive action safety**: Chosen — explicit, target-specific confirmation for every destructive action. Rejected — confirmation only for large batches; undo after execution; implicit execution from toolbar context.
- **Message source selection**: Chosen — active, dead-letter, and transfer dead-letter are explicit selections, with no fallback. Rejected — command labels that silently change a shared default; automatic fallback when a source is unavailable.
- **Receive behavior**: Chosen — peek-lock is the normal receive path; receive-and-delete remains available behind explicit risk confirmation. Rejected — receive-and-delete as the default; excluding receive-and-delete entirely.
- **Recovery**: Chosen — user-selected messages, explicit destination, send-before-settle, and partial outcome reporting. Rejected — automatic whole-source replay; settle-before-send; silent property rewriting.
- **Legacy coexistence**: Chosen — separate modern preview and legacy Windows application during the MVP. Rejected — replacing the legacy application at first preview; expanding the MVP until full legacy parity.
- **Excluded services**: Chosen — Service Bus focus. Rejected — broad multi-service parity in the first MVP; visible non-working shells for excluded services.
- **Duration editing**: Chosen — one compact invariant-format primary field plus an adjacent Edit affordance opening a fully labeled structured popover with typing and keyboard increments. Rejected — a permanently expanded wall of per-component spinner arrows; clipped narrow component fields; subjective visual acceptance such as “attractive” without observable layout criteria.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On each supported operating system, a first-time evaluator can install or extract the preview, launch it, and reach the connection screen in under 5 minutes using published instructions.
- **SC-002**: In acceptance testing, 100% of connection-history and settings records remain free of SAS keys, complete connection strings, Microsoft Entra access tokens, message bodies, and message properties after successful, failed, cancelled, saved, updated, missing-vault, and deleted-credential workflows; a saved SAS profile contains only an opaque random reference.
- **SC-003**: In 100% of automated routing tests, peek, receive, purge, and recovery affect only the explicitly selected active, dead-letter, or transfer dead-letter source.
- **SC-004**: In 100% of tested destructive workflows, no service-changing operation starts before target-specific confirmation; cancelling confirmation produces no change.
- **SC-005**: Evaluators can complete connect, browse, send, peek-lock receive, and one settlement workflow in under 10 minutes after launch using either supported authentication family.
- **SC-006**: Queue, topic, subscription, and rule lifecycle acceptance tests complete with accurate refreshed state for all service-supported operations and clearly identify permission or conflict failures.
- **SC-007**: Session and selected-message recovery tests produce no duplicate automatic retries, preserve the original until replacement send succeeds, and report every partial failure.
- **SC-008**: All P1 workflows can be completed by keyboard alone on each supported operating system, with no keyboard trap and with visible focus at every step.
- **SC-009**: Screen-reader review finds meaningful accessible names and state announcements for 100% of controls in the P1 workflows and all destructive confirmations.
- **SC-010**: Duration round-trip tests preserve the exact value through `D.HH:MM:SS[.fff]` display, structured editing, Apply, Cancel, and Escape, including zero, non-zero milliseconds, and values greater than 365 days.
- **SC-011**: Automated safety and workflow checks pass on every supported change, and each operating-system package passes a launch smoke test before preview publication.
- **SC-012**: During preview, the legacy Windows application remains buildable and separately launchable, and the preview documentation identifies excluded workflows and fallback guidance.
- **SC-013**: On Windows, macOS, and Linux, 100% of SAS persistence acceptance tests verify that saving is disabled by default, explicit opt-in writes only to the designated native vault, update and removal affect the referenced vault entry as requested, and an unavailable or missing vault credential preserves the profile and prompts for SAS with no file fallback.
- **SC-014**: At the documented minimum supported application width and at 100%, 150%, and 200% scaling, all duration layout checks show the complete primary value, five full component labels, numeric values, validation text, and Apply/Cancel actions with zero clipping, overlap, or digits obscured by increment controls.
- **SC-015**: Keyboard-only tests complete open, component traversal, direct typing, Up/Down increment, validation, Apply, Cancel, and Escape flows with the documented logical focus order and focus returning to the Edit affordance in 100% of tested closures.
- **SC-016**: In 100% of invalid-input tests, every invalid duration component has a field-specific accessible error and the bound value remains exactly unchanged from its pre-edit value; in service-limit tests, the general editor retains the representable value while contextual validation names the constrained Azure property.

## Assumptions

- Target users are developers and operators who already have access to an Azure Service Bus namespace and understand the consequences of message settlement and entity administration.
- Service permissions are managed outside this product; the product reflects granted access and does not elevate it.
- Microsoft Entra ID includes the normal interactive or pre-established identity choices available in the user's environment; exact credential selection belongs in planning.
- “Safe SAS” means SAS credentials may be used for a live connection and optionally stored only in the current user's native credential vault after explicit opt-in; they are never saved in application history, settings, logs, crash details, generated support information, plaintext files, or application-managed encrypted files.
- SAS saving is disabled by default for every new or edited profile; prior consent is not inferred for another profile.
- Windows Credential Manager, macOS Keychain Services, and Linux freedesktop Secret Service through libsecret or a compatible provider are the only approved SAS persistence destinations for this MVP.
- Non-secret history and opaque credential references are local to the current operating-system user and have no cloud synchronization requirement in the MVP.
- Opaque credential references are random, reveal no SAS-derived information, and provide no access outside the native vault's authorization boundary.
- Microsoft Entra access tokens are outside the saved-SAS capability and are never persisted by it.
- Message content is potentially sensitive. It is displayed only on deliberate inspection and copied or exported only through an explicit user action.
- Recovery resubmits a replacement message and settles the original only after successful submission when the original is currently settleable; dead-letter messages that cannot be settled after peek are not falsely reported as removed.
- Service-defined limits and mutable settings take precedence over legacy UI ranges; the product explains rejected values.
- The primary duration format is invariant and does not change with operating-system locale; `0.00:00:00`, `12.03:04:05`, and `12.03:04:05.006` are representative valid displays.
- Hours, minutes, seconds, and milliseconds are component fields; service-specific duration constraints are not treated as component ranges or as the editor's general representable range.
- The release must document a minimum supported application width before responsive duration acceptance testing; no visual approval depends on the subjective term “attractive.”
- Bounded retrieval is acceptable for large namespaces and message sources when continuation and refresh are clear.
- English is the only required interface language for the first MVP.
- Supported operating-system versions and package formats will be selected during planning, but Windows, macOS, and Linux must each have a launchable preview artifact.
- No new production support for excluded Azure services is required merely to preserve the legacy application's separate availability.

## Dependencies

- Access to representative Azure Service Bus environments for queues, topics, subscriptions, rules, sessions, dead-lettering, transfer dead-lettering, authorization failures, and throttling.
- Existing Service Bus behavior and contracts may be reused where they satisfy this specification and the project constitution.
- The current user's operating system provides an available native credential vault when optional SAS persistence is desired; absence of a usable vault degrades to manual SAS entry, not alternate persistence.
- Human milestone review is required before advancing from requirements, design, implementation units, testing, and preview release.

## Open Questions

No blocking business questions remain. Package formats, supported operating-system version floors, identity credential selection details, bounded retrieval defaults, and exact confirmation interaction are planning decisions constrained by the requirements above.
