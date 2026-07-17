# UX and Accessibility Requirements Quality Checklist

**Purpose**: Reviewer gate for honest capability presentation, keyboard access, assistive
technology, and cross-platform preview requirement quality  
**Created**: 2026-07-17  
**Feature**: [spec.md](../spec.md)  
**Depth/Audience**: Risk-focused formal design review

## Honest Scope and Workflow States

- [ ] CHK001 Are excluded services required to be absent or disabled with a truthful reason, rather than merely “not supported”? [Clarity, Spec §Explicitly Excluded; Functional Options]
- [ ] CHK002 Are loading, empty, completed, cancelled, stale, partial-success, and failure requirements defined for connection, administration, and messaging? [Completeness, Spec §FR-030]
- [ ] CHK003 Are recovery paths specified for every actionable error category without requiring the user to infer whether retry is safe? [Coverage, Spec §FR-008, FR-030]
- [ ] CHK004 Are bounded retrieval and continuation/refresh expectations visible and unambiguous for large lists? [Clarity, Spec §Edge Cases; Assumptions]
- [ ] CHK005 Are source, scope, capability, and stale-state labels required wherever a user could otherwise mistake the operation context? [Coverage, Spec §FR-004, FR-013, FR-030]

## Keyboard and Focus

- [ ] CHK006 Are keyboard requirements defined for every P1 control class: forms, trees, tables, tabs, source selectors, message actions, cancellation, and confirmations? [Completeness, Spec §FR-031]
- [ ] CHK007 Are logical focus order and visible focus measurable across page transitions and modal dialogs? [Measurability, Spec §FR-031; SC-008]
- [ ] CHK008 Are initial, error, and post-dialog focus destinations specified for critical workflows? [Gap, Spec §FR-031]
- [ ] CHK009 Are keyboard shortcut requirements constrained so destructive shortcuts cannot bypass confirmation? [Safety, Spec §FR-012, FR-031]
- [ ] CHK010 Are no-keyboard-trap expectations defined for message detail, tables, trees, and dialogs on all supported OSs? [Coverage, Spec §SC-008]

## Assistive Technology Semantics

- [ ] CHK011 Are names, roles, values, validation, hierarchy, selection, progress, and state-change requirements mapped to all relevant control types? [Completeness, Spec §FR-032]
- [ ] CHK012 Are message source, receive kind, settlement eligibility, truncation, and session ownership required as programmatic semantics, not visual text alone? [Clarity, Spec §FR-022, FR-024, FR-032]
- [ ] CHK013 Are announcement priority and timing specified for progress, cancellation, partial success, and actionable errors? [Gap, Spec §FR-030, FR-032]
- [ ] CHK014 Are destructive confirmation semantics complete for dialog title, target, source, consequence, default action, and cancellation? [Coverage, Spec §FR-012, FR-032]
- [ ] CHK015 Is the prohibition on color-only meaning explicit for source, status, selection, warning, and failure? [Gap, Spec §FR-031–FR-032]
- [ ] CHK016 Can SC-009 be evaluated against an explicit inventory of P1 controls rather than an undefined percentage denominator? [Measurability, Spec §SC-009]

## Content, Duration, and Packaging

- [ ] CHK017 Are empty, text, structured, binary, malformed, and truncated message representations specified with metadata preservation? [Completeness, Spec §FR-022]
- [ ] CHK018 Are sensitive copy/export warnings and deliberate-action requirements consistent for keyboard and assistive-technology users? [Consistency, Spec §FR-021, FR-031–FR-032]
- [ ] CHK019 Are duration component ranges, invariant formatting, validation, and exact round-trip expectations specified through milliseconds and beyond 365 days? [Clarity, Spec §FR-033, FR-036–FR-040; SC-010]
- [ ] CHK020 Are OS, architecture, version, preview status, signing status, launch steps, and known limitations required for each package? [Completeness, Spec §FR-034]
- [ ] CHK021 Are supported OS floors and package formats recorded as reviewable planning decisions rather than left implicit? [Dependency, Spec §Assumptions]
- [ ] CHK022 Is legacy Windows fallback wording required to distinguish coexistence from automatic replacement or migration? [Consistency, Spec §FR-002; SC-012]
- [ ] CHK023 Are accessibility acceptance requirements defined on each supported OS with named assistive-technology evidence expectations? [Coverage, Spec §User Story 5; SC-008–SC-009]

## Native Vault Experience

- [ ] CHK024 Is the SAS-save control required to start off for every new or edited profile and to name the current platform vault? [Clarity, Spec §User Story 1; FR-006]
- [ ] CHK025 Are successful connection and failed credential persistence represented as separate outcomes so the UI cannot claim reconnect was saved? [State Clarity, Spec §Edge Cases; FR-007]
- [ ] CHK026 Are unavailable, locked, denied, provider-missing, unsupported, and missing-credential states given distinct actionable recovery copy that preserves the profile? [Coverage, Spec §User Story 1 AC-6; FR-007]
- [ ] CHK027 Is manual SAS use after retrieval failure distinguished from explicit replacement of the saved credential? [Interaction Clarity, Spec §User Story 1 AC-7; FR-007]
- [ ] CHK028 Does profile removal require a separate, understandable choice for native-vault cleanup and explain partial failure? [Completeness, Spec §User Story 1 AC-8; FR-007]
- [ ] CHK029 Are vault prompts, permissions, errors, and cleanup choices keyboard-operable and exposed with meaningful assistive-technology semantics? [Accessibility, Spec §FR-031–FR-032, FR-035]
- [ ] CHK030 Is Entra wording consistent that no access token is saved by this feature and no SAS-save control applies to Entra profiles? [Consistency, Spec §User Story 1 AC-9; FR-006]

## Compact Duration Editor Amendment

- [ ] CHK031 Is the primary duration field required to occupy one form row and use exactly `D.HH:MM:SS[.fff]`, including fixed-width component rules and millisecond omission only when zero? [Measurability, Spec §User Story 5 AC-4; FR-036]
- [ ] CHK032 Is the adjacent Edit affordance required to open a structured popover with persistent full labels for Days, Hours, Minutes, Seconds, and Milliseconds? [Completeness, Spec §User Story 5 AC-6; FR-037]
- [ ] CHK033 Are direct numeric typing and keyboard Up/Down increments required without permanently visible spinner arrows in the primary form? [Interaction, Spec §FR-037; Duration Editing Decision]
- [ ] CHK034 Are component ranges and field-specific errors explicit enough to test empty, malformed, negative, fractional, and out-of-range values without mutating the bound duration? [Validation, Spec §FR-039; SC-016]
- [ ] CHK035 Are Escape, Cancel, Apply, and all popover-close paths specified with exact commit/discard behavior and focus returning to the Edit affordance? [Keyboard/Focus, Spec §User Story 5 AC-7–AC-8; FR-038]
- [ ] CHK036 Are accessible name, current value, format help, field labels, and error semantics required for both the compact field and structured editor? [Assistive Technology, Spec §FR-038]
- [ ] CHK037 Is the editor's general representable range explicitly separated from contextual Azure-property limits without silent clamping or mutation? [Range Semantics, Spec §FR-040; SC-016]
- [ ] CHK038 Are minimum-width and 100%, 150%, and 200% scaling checks required to show complete values, full labels, validation, and actions with zero clipping or overlap? [Responsive Measurability, Spec §FR-041; SC-014]
- [ ] CHK039 Does regression coverage explicitly prevent numeric values from disappearing behind increment or spinner controls? [Regression, Spec §FR-035, FR-041; SC-014]
- [ ] CHK040 Is “compact and scannable” measured by one-row complete-value display, no permanent arrow wall, full labels on demand, and zero clipping rather than subjective attractiveness? [Visual Acceptance, Spec §FR-036–FR-037; Scope Decision; SC-014]
- [ ] CHK041 Is the shared whole-millisecond range defined with exact endpoints so “representable” can be tested independently from Azure property constraints? [Measurability, Spec §FR-033, FR-040]
- [ ] CHK042 Are Send-page availability and destination behavior independently testable rather than hidden inside DurationEditor or broader messaging acceptance? [Separation, Spec §User Story 0; FR-043; SC-018]

## First Internal Version Experience

- [ ] CHK043 Are queue and topic Send pages required to render the existing composer and expose the current send action, including actionable failure without losing the draft? [Availability, Spec §User Story 0 AC-4; User Story 2 AC-8–AC-9; FR-043]
- [ ] CHK044 Does subscription Send identify its parent topic as the actual destination before submission and in success/failure outcomes, both visually and programmatically, with no direct-subscription-send wording? [Truthful Context, Spec §User Story 0 AC-5; User Story 2 AC-10; FR-043; SC-018]
- [ ] CHK045 Does a reviewed inventory account for every duration input on currently visible Service Bus entity forms, with no inventoried form retaining the broken stepper? [Coverage, Spec §User Story 0 AC-6; FR-044; SC-019]
- [ ] CHK046 Does development/test-only labeling communicate incomplete parity without implying that plaintext credential history is present or permitted? [Truthful Status, Spec §User Story 0 AC-10; FR-046; SC-021]
- [ ] CHK047 Are the three internal slices demonstrated and reported separately so failure in dead-letter safety, Send availability, or DurationEditor coverage cannot be masked by aggregate success? [Evidence Clarity, Spec §FR-045; SC-017–SC-020]
- [ ] CHK048 Do first-internal profile and reconnect experiences clearly state that SAS is not saved, prompt for it on every connection, and expose that prompt accessibly without displaying the secret? [Credential UX, Spec §User Story 0 AC-8–AC-9; FR-046; SC-021]
- [ ] CHK049 Does the internal artifact visibly distinguish development-run/single-host status from final cross-platform preview packaging, including revision and limitations without claiming package readiness? [Milestone Truthfulness, Spec §User Story 0 AC-10; First Internal Version Boundary]
- [ ] CHK050 Do Send accessibility tests announce requested subscription context and actual parent-topic destination without creating duplicate queue/topic/subscription composer views? [Accessible Context, Spec §FR-043; SC-018]
