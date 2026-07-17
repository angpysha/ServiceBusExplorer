# Security and Safety Requirements Quality Checklist

**Purpose**: Reviewer gate for security, irreversible operations, routing, and recovery requirement
quality  
**Created**: 2026-07-17  
**Feature**: [spec.md](../spec.md)  
**Depth/Audience**: Risk-focused formal design review

## Credential and Data Protection

- [ ] CHK001 Are persisted profile and settings fields exhaustively allowlisted, including the optional opaque random credential reference, with all credential-derived data explicitly prohibited? [Completeness, Spec §FR-005–FR-006]
- [ ] CHK002 Are failed, cancelled, corrupt-history, unavailable-vault, locked-vault, denied-vault, deleted-credential, crash, and support-diagnostic paths covered by the same no-secret-outside-vault rule? [Coverage, Spec §FR-006–FR-007; Edge Cases]
- [ ] CHK003 Is “routine diagnostics” bounded clearly enough to prohibit raw SDK exception data that may echo inputs? [Clarity, Spec §FR-008]
- [ ] CHK004 Are SAS and Entra reconnect requirements distinct and measurable, with optional SAS vault retrieval separated from mandatory non-persistence of Entra access tokens? [Clarity, Spec §FR-003, FR-006–FR-007]
- [ ] CHK005 Are tenant, namespace, entity scope, and loading-option requirements consistent across profile, connect, and capability behavior? [Consistency, Spec §FR-003–FR-005]
- [ ] CHK006 Does legacy-history handling permit only verified non-secret metadata derivation or removal, while prohibiting raw values from being rendered, copied into profiles/vaults, or retained as a reconnect fallback? [Boundary, Spec §Edge Cases; FR-006, FR-046]

## Source Routing and Destructive Actions

- [ ] CHK007 Is the absence of an explicit source defined separately from active source in every destructive workflow? [Clarity, Spec §FR-013]
- [ ] CHK008 Are active, dead-letter, and transfer-dead-letter requirements complete for peek, receive, purge, and recovery? [Coverage, Spec §FR-016–FR-020, FR-026]
- [ ] CHK009 Is unavailable transfer-dead-letter behavior specified without fallback or misleading success? [Edge Case, Spec §Edge Cases]
- [ ] CHK010 Are confirmation contents specified for entity, source, consequence, destination, item count, and irreversible-loss risk where applicable? [Completeness, Spec §FR-012, FR-017, FR-026–FR-028]
- [ ] CHK011 Is confirmation cancellation objectively defined as no service-changing operation beginning? [Measurability, Spec §FR-012; SC-004]
- [ ] CHK012 Are receive-and-delete display-loss consequences sufficiently explicit for informed consent? [Clarity, Spec §FR-017; Edge Cases]
- [ ] CHK013 Are interrupted purge outcomes required to distinguish confirmed removal from uncertainty? [Coverage, Spec §FR-020, FR-028]

## Settlement, Sessions, and Recovery

- [ ] CHK014 Are all settlement ineligibility states named, including peeked, expired, already settled, and ownership lost? [Completeness, Spec §FR-019]
- [ ] CHK015 Is repeated settlement prohibited with measurable state-transition criteria? [Measurability, Spec §User Story 2 AC-4]
- [ ] CHK016 Are session lock loss and reacquisition requirements complete without allowing silent continuation? [Recovery, Spec §FR-023–FR-024]
- [ ] CHK017 Is send-before-settle ordering explicit for both settleable and peek-only recovery originals? [Clarity, Spec §FR-026–FR-027; Assumptions]
- [ ] CHK018 Are diagnostic-property treatment choices specified without silent rewriting? [Completeness, Spec §FR-027]
- [ ] CHK019 Are partial-success and retry requirements precise enough to exclude automatic repetition of confirmed successes? [Measurability, Spec §FR-028; SC-007]

## Failure and Authorization Boundaries

- [ ] CHK020 Are authentication, authorization, validation, conflict, throttling, outage, cancellation, stale, partial, and unknown outcomes distinguishable? [Coverage, Spec §FR-008, FR-029–FR-030]
- [ ] CHK021 Are entity-scoped capability limitations specified without implying namespace authorization? [Consistency, Spec §FR-004; Edge Cases]
- [ ] CHK022 Are retry requirements bounded for destructive operations after an uncertain outcome? [Gap, Spec §FR-028–FR-030]
- [ ] CHK023 Are application-close requirements complete for in-flight operations whose outcomes may be uncertain? [Recovery, Spec §Edge Cases]
- [ ] CHK024 Can SC-002, SC-003, SC-004, and SC-007 be verified without exposing real credentials or message content in evidence? [Acceptance Criteria, Spec §Success Criteria]

## Native Vault Amendment

- [ ] CHK025 Is SAS saving explicitly opt-in and disabled by default for every new or edited profile, without inferring consent from another profile? [Safety Default, Spec §User Story 1; FR-006; Assumptions]
- [ ] CHK026 Are the only permitted persistence destinations mapped completely to Windows Credential Manager, macOS Keychain Services, and Linux freedesktop Secret Service via libsecret or a compatible provider? [Completeness, Spec §FR-006]
- [ ] CHK027 Does the specification prohibit both plaintext and application-managed encrypted-file fallback when the native vault is unavailable, locked, denied, unsupported, or missing the referenced entry? [Fallback Safety, Spec §FR-006–FR-007; Edge Cases]
- [ ] CHK028 Are save, update, reconnect, profile removal, optional vault-entry deletion, and differing partial outcomes defined across the full saved-credential lifecycle? [Lifecycle Coverage, Spec §User Story 1; FR-007; SC-013]
- [ ] CHK029 Is the credential reference required to be opaque, random, non-secret, non-derived from SAS data, and useless outside the native vault authorization boundary? [Reference Safety, Spec §FR-005; Key Entities; Assumptions]
- [ ] CHK030 Do acceptance outcomes verify that a missing or inaccessible vault entry preserves the non-secret profile and prompts for SAS again? [Recovery, Spec §User Story 1; FR-007; SC-013]
- [ ] CHK031 Can platform vault acceptance evidence prove correct storage location and lifecycle behavior without printing or otherwise exposing the real SAS connection string? [Test Safety, Spec §FR-035; SC-002; SC-013]
- [ ] CHK032 Are vault availability and failure categories complete enough to distinguish unavailable, locked, denied, provider-missing, unsupported, missing-item, cancelled, and unknown failure? [Clarity, Spec §FR-007; Edge Cases]
- [ ] CHK033 Are replacement ordering and failure semantics explicit about preserving the prior credential/reference unless replacement succeeds? [Lifecycle Safety, Spec §User Story 1 AC-7; FR-007]
- [ ] CHK034 Are profile deletion and optional vault cleanup specified as separate user choices with recoverable partial outcomes? [Completeness, Spec §User Story 1 AC-8; FR-007]
- [ ] CHK035 Does package-selection guidance require license, maintenance, native-code, transitive dependency, fallback, supply-chain, and packaged cross-platform smoke review before adoption? [Dependency Risk, Constitution §Technical Constraints]
- [ ] CHK036 Is any in-memory vault limited explicitly to tests and prohibited from production composition as a persistence fallback? [Fallback Safety, Spec §FR-006–FR-007]

## First Internal Version Security Baseline

- [ ] CHK037 Is the current raw connection-string history write path required to be disabled or replaced before first-internal distribution, with no environment-based exception? [Security Baseline, Spec §First Internal Version Boundary; FR-046]
- [ ] CHK038 Do successful, failed, cancelled, repeat, and post-restart tests prove that first-internal history/settings contain only the FR-005 non-secret allowlist and no SAS secret, credential-derived value, credential reference, or Entra access token? [Coverage, Spec §User Story 0 AC-8; FR-046; SC-021]
- [ ] CHK039 Is saved-SAS reconnect explicitly unavailable until native-vault support lands, requiring full SAS re-entry for every first-internal connection? [Persistence Boundary, Spec §User Story 0 AC-9; FR-046; SC-021]
- [ ] CHK040 Does internal exit evidence prove absent-source blocking, exact dead-letter routing, target-specific purge confirmation, and cancellation with no service-changing operation? [P0 Safety, Spec §FR-042; SC-017]
- [ ] CHK041 Is development/test-only labeling tied solely to incomplete feature parity and explicitly prohibited from excusing plaintext or weaker credential storage? [Truthfulness, Spec §User Story 0 AC-10; FR-046; SC-021]
- [ ] CHK042 Does the first-internal gate block executable sharing until the raw writer is removed, legacy canary history is sanitized/removed, read-back inspection passes, and a security-focused human review approves the result? [Gate Ordering, Spec §FR-046; Plan §Delivery Slices]
- [ ] CHK043 Is `ICredentialVault`, credential-reference schema, and saved-SAS UI explicitly absent—not merely disabled by convention—from first-internal production composition? [Scope Safety, Spec §First Internal Version Boundary; FR-046]
