<!--
Sync Impact Report
- Version change: template -> 1.0.0
- Modified principles: placeholder principles -> five project-specific principles
- Added sections: Technical and Security Constraints; Development Workflow and Quality Gates
- Removed sections: none
- Templates:
  - ✅ .specify/templates/plan-template.md (Constitution Check remains the enforcement gate)
  - ✅ .specify/templates/spec-template.md (testing and measurable outcomes already mandatory)
  - ✅ .specify/templates/tasks-template.md (test policy aligned)
  - ✅ .specify/templates/commands/*.md (directory absent; no files to update)
- Runtime guidance:
  - ✅ PROJECT.md
  - ✅ AGENTS.md
  - ⚠ CLAUDE.md contains legacy architecture details and requires a separate documentation refresh
- Deferred items: none
-->
# ServiceBusExplorer Constitution

## Core Principles

### I. Avalonia Is the Product UI
All new cross-platform user-facing functionality MUST be implemented in the .NET 10 Avalonia
application. Business behavior MUST live outside UI code so it can be tested and reused. The
WinForms application MAY receive narrowly scoped compatibility or critical fixes while migration
continues, but it MUST NOT become the source of new product architecture. A feature is considered
migrated only when its required behavior is available and verified in Avalonia.

### II. Preserve Layer Boundaries
Dependencies MUST flow from App to ViewModels and Services/Core, with Azure SDK and infrastructure
details kept out of views and presentation state. Core models and business rules MUST remain
framework-independent. New coupling to WinForms, global mutable state, or UI-thread-specific APIs
outside the App layer requires an explicit design justification.

### III. Secure Modern Azure Integration
New Azure integrations MUST use supported modern Azure SDKs and Azure Identity. Credentials,
tokens, connection strings, and message contents MUST NOT be logged or committed. Authentication
and authorization changes MUST include threat analysis and tests for failure paths. Dependencies on
deprecated Azure SDKs are permitted only in untouched legacy code or through a documented,
time-bounded migration exception.

### IV. Tests Define Completion
Every behavior change MUST include automated tests at the lowest effective level. Contract,
integration, or UI tests MUST be added when unit tests cannot verify Azure SDK boundaries,
serialization, concurrency, or user workflows. Bug fixes MUST include a regression test when the
failure is reproducible. Documentation-only and mechanical changes MAY omit tests when the reason
is recorded. Builds and tests MUST pass with warnings treated as errors before merge.

### V. Async, Observable, and Resilient Operations
Network and messaging operations MUST be asynchronous and cancellable; sync-over-async is
prohibited. Expected failures MUST produce actionable, secret-safe diagnostics and leave the UI
responsive. Retry, timeout, and concurrency behavior MUST be explicit for operations that contact
Azure services. Logging MUST identify the operation and outcome without exposing sensitive data.

## Technical and Security Constraints

- The modern application targets .NET 10, Avalonia 11, and ReactiveUI on Windows, macOS, and Linux.
- Nullable reference types and modern C# constructs MUST be used in new modern-project code.
- Public APIs MUST include XML documentation.
- Package additions MUST be justified, supported, and checked for overlap with existing
  dependencies before adoption.
- Platform-specific behavior MUST be isolated behind an interface and verified on each affected
  platform.
- Infrastructure provisioning MUST use reviewed infrastructure-as-code after its scope and tool are
  selected; credentials MUST use GitHub and Azure secret or identity mechanisms.

## Development Workflow and Quality Gates

1. Work MUST be tracked in beads and linked to the applicable specification artifacts.
2. Feature work MUST proceed through specification, plan, and dependency-ordered tasks.
3. Plans MUST pass the Constitution Check before implementation and after design.
4. Codebase discovery and deduplication MUST follow the repository's documented search protocol.
5. Pull requests MUST pass build, test, security, and documentation checks applicable to the
   changed area.
6. Migration work MUST state which legacy behavior is preserved, replaced, or intentionally
   removed.
7. Complexity or a constitutional exception MUST be documented with the rejected simpler
   alternative and an owner for follow-up.

## Governance

This constitution supersedes conflicting development guidance. Amendments require a documented
rationale, impact assessment, updated dependent templates, and maintainer approval. Versions follow
semantic versioning: MAJOR for incompatible principle or governance changes, MINOR for new
principles or materially expanded obligations, and PATCH for clarifications. Every plan and pull
request review MUST verify applicable principles. Approved exceptions MUST identify scope, owner,
expiry or removal criteria, and compensating controls. `PROJECT.md`, `AGENTS.md`, and repository
guidance provide operational context but cannot weaken this constitution.

**Version**: 1.0.0 | **Ratified**: 2026-07-16 | **Last Amended**: 2026-07-16
