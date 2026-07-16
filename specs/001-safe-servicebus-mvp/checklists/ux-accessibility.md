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
- [ ] CHK019 Are duration field ranges, normalization, validation, and exact round-trip expectations specified through milliseconds and beyond 365 days? [Clarity, Spec §FR-033; SC-010]
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
