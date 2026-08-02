# Feature Specification: Preview Installer Packaging (macOS notarized + Windows MSI)

**Feature Branch**: `feature/avalonia-servicebus-mvp` (spec dir `002-preview-installer-packaging`; may later use a dedicated branch)

**Created**: 2026-08-02

**Status**: Draft (grill clarifications resolved 2026-08-02; **plan complete** 2026-08-02)

**Input**: User description: "Prefer fastlane notarize for macOS packaging (https://docs.fastlane.tools/actions/notarize/). For Windows, MSI creation is enough. Create a Spec Kit spec and grill it."

**Related**: Supplements `specs/001-safe-servicebus-mvp` US5 / packaging gate (T029–T030). Where this spec and 001 disagree on installer formats, **this spec wins for installer delivery**; 001 remains canonical for product behavior.

## Clarifications

### Session 2026-08-02

- Q: Is a Mac App Store / provisioning profile required for the macOS preview pipeline? → A: No — only Developer ID Application `.p12` plus App Store Connect API Key (`.p8` + Key ID + Issuer ID); no provisioning profile.
- Q: macOS packaging order — unsigned then notarize, or sign first? → A: Sign with Developer ID → create DMG → notarize and staple (not unsigned→notarize).
- Q: Which macOS architectures in the first CI release? → A: **osx-arm64 only** first; osx-x64 deferred (Intel Mac support winding down).
- Q: How to communicate Intel Mac unsupported? → A: Explicit **Apple Silicon only** disclaimer in `docs/preview-installation.md` and README.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Install Avalonia preview on Windows via MSI (Priority: P1)

An evaluator on Windows downloads a single MSI for the modern Avalonia Service Bus Explorer preview, installs it with a normal Windows installer UX (per-machine or per-user as defined below), finds the app in Start Menu / uninstall list, launches it, and can remove it cleanly via Add/Remove Programs.

**Why this priority**: Windows remains the primary desktop audience; zip-only distribution is a barrier for non-developer evaluators and for Winget/enterprise trial paths later.

**Independent Test**: Build the MSI from CI or a documented local script; install on a clean Windows 10/11 VM; launch; uninstall; confirm no residual Start Menu entry and documented cleanup expectations.

**Acceptance Scenarios**:

1. **Given** a published preview MSI and matching checksum, **When** the evaluator installs it with default options, **Then** the Avalonia app launches from the Start Menu without requiring a separate .NET runtime install (self-contained).
2. **Given** an installed preview, **When** the evaluator uninstalls via Windows Settings → Apps, **Then** the product is removed from the apps list and the documented install directory is gone (or emptied per MSI rules).
3. **Given** MSI metadata (product name, version, publisher), **When** inspected in Programs and Features / Apps, **Then** version and preview labeling match the release notes for that build.
4. **Given** the MSI wizard, **When** the evaluator chooses per-user install (no elevation), **Then** the app installs and launches for that user without requiring administrator rights.
5. **Given** the MSI wizard, **When** the evaluator chooses per-machine install and elevates, **Then** the app is available machine-wide (e.g. Program Files + All Users Start Menu as documented).

---

### User Story 2 - Install Avalonia preview on macOS without Gatekeeper fight (Priority: P1)

An evaluator on macOS 13+ downloads the **primary** preview **DMG**, opens it, drags **Service Bus Explorer** to Applications, and launches without needing `xattr` quarantine removal or right-click Open workarounds, because the build is Developer ID–signed, notarized, and stapled. A zip-of-`.app` MAY exist for automation but is not the evaluator-facing primary download.

**Why this priority**: Unnotarized internal builds are blocked or friction-heavy on modern macOS; notarization is required for credible macOS preview distribution.

**Independent Test**: On an **Apple Silicon** Mac that did not build the artifact, download from CI/release, verify checksum, open DMG, drag to Applications, launch; `spctl --assess` reports accepted / notarized as documented.

**Acceptance Scenarios**:

1. **Given** a notarized and stapled macOS preview artifact, **When** opened on a fresh Mac with Gatekeeper enabled, **Then** the app launches without quarantine bypass commands.
2. **Given** signing/notarization credentials are configured for CI, **When** the packaging pipeline runs successfully, **Then** the published macOS artifact records notarized status in its release manifest.
3. **Given** notarization fails (Apple rejects) or required Apple secrets are missing while notarize mode is enabled, **When** the packaging job finishes, **Then** the macOS job **fails** and does **not** upload or attach a macOS artifact to the preview/notarized release channel. A separate, explicitly named debug/ad-hoc packaging mode MAY exist later but MUST NOT be the default for release tags or “notarize enabled” runs, and MUST label artifacts `signing=ad-hoc` / `notarization=not notarized` if ever produced.

---

### User Story 3 - Maintainer publishes preview packages from GitHub Actions (Priority: P2)

A maintainer triggers packaging (manual workflow and/or version tag), obtains Windows MSI + macOS notarized installer artifacts (and optionally Linux archive), with checksums and a short install note, without running Apple notarization steps by hand on a laptop.

**Why this priority**: Repeatable CI packaging is what makes preview releases sustainable; local scripts remain a fallback.

**Independent Test**: Run the packaging workflow with secrets configured (or documented dry-run); download artifacts; verify manifests and checksums.

**Acceptance Scenarios**:

1. **Given** repository secrets for Apple notarization are present, **When** the maintainer runs the packaging workflow with notarize enabled, **Then** macOS artifacts are produced via the project’s packaging lane that performs notarization (fastlane `notarize` action per project plan) and appear as workflow artifacts or a draft prerelease.
2. **Given** Windows packaging inputs are valid, **When** the Windows job runs, **Then** an MSI plus SHA-256 sidecar are produced.
3. **Given** a consumer reads the preview install doc, **When** they follow OS-specific steps, **Then** they can install without undocumented tribal knowledge.

---

### User Story 4 - Linux remains archive-based (Priority: P3)

A Linux evaluator downloads a self-contained `linux-x64` archive, extracts it, and runs the binary, with Secret Service prerequisites documented (unchanged from 001 intent). No native `.deb`/`.rpm` is required in this feature.

**Why this priority**: Useful but secondary; MSI + notarized macOS are the new gaps.

**Independent Test**: Extract tar.gz on Ubuntu 22.04+; launch; confirm documented vault prerequisites.

**Acceptance Scenarios**:

1. **Given** the linux-x64 archive from the same preview pipeline, **When** extracted and launched on a supported distro, **Then** the app starts (GUI session assumed).

---

### Edge Cases

- Notarization service timeout or intermittent Apple outage during CI.
- Apple credentials present but Developer ID certificate expired or wrong type (e.g. development cert instead of Developer ID Application).
- MSI upgrade from a previous preview version (same UpgradeCode vs new product).
- Evaluator installs MSI without admin rights (per-user vs per-machine).
- Cross-architecture macOS: Intel (x64) Macs are **unsupported** for this feature’s DMG; docs MUST state Apple Silicon only.
- Partial secret set (certificate without notary credentials) — job must not claim notarized.
- Compromised or rotated secrets mid-release.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The project MUST produce a Windows preview **MSI** for the Avalonia app (**win-x64**) that installs a self-contained build (no separate .NET desktop runtime requirement for evaluators). A **win-arm64** MSI is out of scope for this feature and MAY follow later.
- **FR-002**: The MSI MUST expose product name, version, and preview identity suitable for Add/Remove Programs and MUST support clean uninstall.
- **FR-003**: The MSI MUST support **both per-machine and per-user** installation, selectable in the installer UI (wizard). Per-machine requires elevation and installs under Program Files (or equivalent); per-user installs without admin into the user profile and registers Start Menu / uninstall entries for that user. Both paths MUST be covered by install/uninstall acceptance checks.
- **FR-004**: The project MUST produce a macOS preview **DMG** for **osx-arm64** as the primary evaluator download. Pipeline order MUST be: self-contained publish → assemble `.app` → **Developer ID codesign** (hardened runtime) → create DMG → **notarize and staple**. Apple does **not** notarize an unsigned or merely ad-hoc–signed app for this distribution path. An **osx-x64** DMG is out of scope for this feature and MAY follow later. Zip-of-`.app` is optional and non-primary.
- **FR-005**: macOS notarization in CI MUST be driven through **fastlane**’s [`notarize`](https://docs.fastlane.tools/actions/notarize/) action (or an equivalent thin wrapper lane that calls that action), not ad-hoc one-off shell-only notarization as the primary path.
- **FR-006**: Packaging MUST authenticate to Apple notarization in CI primarily via an **App Store Connect API Key** (key id, issuer id, and private key material supplied only through CI secrets / ephemeral files — never committed). Local maintainer docs MAY document Apple ID + app-specific password as an optional fallback for laptop builds; CI “notarize enabled” runs MUST use the API key path. A Mac App Store or other **provisioning profile is NOT required** and MUST NOT be treated as a prerequisite for this outside-App-Store Developer ID + notarize pipeline.
- **FR-007**: Every published installer/archive MUST ship a SHA-256 checksum and a machine-readable or human-readable manifest stating version, OS/arch, signing status, and notarization status (macOS).
- **FR-008**: Preview install documentation MUST describe Windows MSI install/uninstall, macOS DMG install + Gatekeeper expectations, and Linux archive + Secret Service notes. macOS docs and README MUST state clearly that this preview supports **Apple Silicon (osx-arm64) only** and that Intel Macs are not supported in this feature.
- **FR-009**: Failed notarization or missing Apple credentials in notarize mode MUST fail the macOS packaging job (**fail closed**). The job MUST NOT upload macOS artifacts to the preview/notarized release channel in that case, and MUST NOT label any artifact as notarized.
- **FR-010**: Linux preview MAY continue as `linux-x64` compressed archive only; native Linux packages are out of scope.
- **FR-011**: Legacy WinForms / Chocolatey Windows packaging remains out of scope for this feature (existing release path may continue separately).
- **FR-012**: Windows Authenticode (code-signing the MSI/exe) is **out of scope for this feature**. Preview MSI MAY ship **unsigned**. The release manifest and preview install docs MUST state `signing=unsigned` (or equivalent) and warn about SmartScreen / “Unknown publisher” prompts. Authenticode MAY be added in a later feature when a code-signing certificate is available.
- **FR-013**: Windows MSI packaging MUST use **WiX Toolset v4+** (project sources + CI on a Windows runner). Commercial installer authors are out of scope.

### Key Entities

- **Preview Artifact**: A versioned installable or archive for one OS/arch, with checksum and status fields (signed / notarized / unsigned).
- **Release Manifest**: Summary of artifacts for one preview build (version, RIDs, signing/notarization states, links or filenames).
- **Notarization Result**: Apple acceptance/rejection outcome bound to a specific macOS artifact revision (for CI evidence).
- **MSI Product Identity**: ProductName, ProductVersion, UpgradeCode/ProductCode policy for upgrades across previews.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new Windows evaluator can install from MSI and launch the Avalonia preview in under 5 minutes without installing a separate .NET SDK/runtime.
- **SC-002**: A new macOS evaluator can install from the published artifact and launch under Gatekeeper without quarantine-removal commands on a machine that did not produce the build.
- **SC-003**: Maintainers can produce Windows MSI + macOS notarized artifact + Linux archive from one documented CI entrypoint with zero manual Apple UI clicks.
- **SC-004**: 100% of artifacts attached to a “notarized/preview installer” release include checksums and an explicit signing/notarization status string.
- **SC-005**: Uninstall of the Windows MSI removes the app from the Windows apps list in one standard uninstall action.

## Assumptions

- Avalonia app under `src/App` remains the packaged binary; this feature does not change in-app messaging behavior.
- Maintainers hold an Apple Developer Program membership and can create Developer ID Application certificates and notarization credentials.
- fastlane is an accepted packaging dependency for macOS CI (Ruby toolchain on macOS runners). Windows MSI MUST be built with **WiX Toolset v4+** (open-source) in CI; commercial installer IDEs are out of scope for MVP.
- Self-contained publish remains the distribution model (consistent with 001 preview packaging research).
- Early preview ships **osx-arm64** DMG only; **osx-x64** is deferred (clarify 2026-08-02: B — Intel Mac support ending).
- Windows Authenticode is deferred for cost reasons; unsigned MSI is an accepted preview trade-off (grill 2026-08-02: option B).
- MSI install scope is dual: per-machine and per-user selectable in the wizard (grill 2026-08-02: option C).
- Notarize-enabled CI is fail-closed: no macOS artifact upload on notarization/credential failure (grill 2026-08-02: option A).
- CI notarization auth primary = App Store Connect API Key; Apple ID app-password is local fallback only (grill 2026-08-02: option B).
- Primary macOS evaluator download is a notarized/stapled **DMG**; zip-of-app is optional/non-primary (grill 2026-08-02: option A).
- Windows MSI tooling is **WiX Toolset v4+** (grill 2026-08-02: option A).
- Windows MSI architecture for this feature is **win-x64 only**; win-arm64 deferred (grill 2026-08-02: option A).
- Outside-App-Store macOS preview needs **no provisioning profile** (clarify 2026-08-02: A); credentials are Developer ID `.p12` + ASC API `.p8` (+ IDs).
- macOS order is **sign with Developer ID, then notarize** — not “unsigned build then notarize”.
- Existing `scripts/package-macos-internal.sh` / `publish-preview.ps1` / `preview-packages.yml` are starting points to **replace or wrap** so fastlane becomes the notarization source of truth—not a second parallel unsigned path marketed as equivalent.
- Chocolatey / Winget submission for the Avalonia MSI is **out of scope** for this feature (may follow later).

## Out of Scope

- Mac App Store / sandboxed distribution (and any Mac App Store provisioning profile).
- Microsoft Store / MSIX-only strategy (MSI is the Windows deliverable).
- Linux `.deb` / `.rpm` / Flatpak / AppImage.
- Signing/notarization of the legacy WinForms EXE.
- Auto-update / sparkle / Windows Update servicing.
- Full enterprise GPO / Intune packaging guides (beyond MSI being Intune-friendly in principle).
