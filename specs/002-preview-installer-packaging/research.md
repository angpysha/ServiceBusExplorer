# Research: Preview Installer Packaging

**Feature**: `002-preview-installer-packaging` | **Date**: 2026-08-02

## R1 — macOS notarization via fastlane

**Decision**: Use fastlane’s [`notarize`](https://docs.fastlane.tools/actions/notarize/) action as the CI notarization step after Developer ID signing and DMG creation. Authenticate with **App Store Connect API Key** (`api_key` / `api_key_path` parameters). Staple on success. Fail the job and skip artifact upload on failure.

**Rationale**: Stakeholder requirement; documented parameters include `package`, `bundle_id`, `api_key_path`, `skip_stapling`. API key avoids interactive MFA and is better for GitHub Actions than Apple ID + app-specific password (local fallback only).

**Alternatives considered**:
- Raw `xcrun notarytool` in shell (current scripts) — workable but FR-005 rejects it as primary.
- `altool` — retired by Apple.
- Notarize only the zip-of-app and skip DMG notarization — worse evaluator UX; DMG is primary (grill Q5).

## R2 — Signing order for Avalonia .app + DMG

**Decision**: Sign nested Mach-O under `Contents/MacOS` with hardened runtime + `src/App/Entitlements.plist`, then sign the `.app`, build UDZO DMG, notarize via fastlane (package = DMG or zip-of-app then staple as Apple requires), staple, verify with `spctl`/`stapler validate`. Order is **never** “unsigned publish → notarize”.

**Rationale**: Matches Avalonia deployment guidance; Apple requires Developer ID signature before notarization; Apple discourages relying solely on `codesign --deep`. Confirmed in `/speckit-clarify` 2026-08-02.

**Alternatives considered**: Ad-hoc sign for CI without secrets — allowed only outside notarize-enabled mode; never for release channel (fail-closed). Unsigned→notarize — rejected by Apple / clarify Q2.

## R3 — Windows MSI with WiX v4+ dual scope

**Decision**: Author a **dual-purpose** MSI per [Single Package Authoring](https://learn.microsoft.com/en-us/windows/win32/msi/single-package-authoring): `ALLUSERS=2` with UI to choose per-user vs per-machine (`MSIINSTALLPERUSER`). Prefer WiX Package scope `perUserOrMachine` (or equivalent WiX v4+ property authoring). Default to **per-user** when double-clicked (Microsoft recommendation). Payload = `dotnet publish -r win-x64 --self-contained` output harvested into WiX.

**Rationale**: Grill Q2 requires both scopes; WiX v4+ is FOSS and CI-friendly (grill Q6); win-x64 only (grill Q7).

**Alternatives considered**:
- Two separate MSIs — more release churn.
- MSIX / Store — out of scope.
- Per-machine only — rejected in grill.
- Commercial Advanced Installer — out of scope.

## R4 — Authenticode deferred

**Decision**: Ship unsigned MSI; MANIFEST and docs state `signing=unsigned` and SmartScreen warning.

**Rationale**: Cost (grill Q1-B).

## R5 — CI workflow shape

**Decision**: Evolve `.github/workflows/preview-packages.yml`:
- **windows-2022**: publish + WiX → MSI + sha256 + MANIFEST fragment; always upload on success.
- **macos-14 (arm64)**: import Developer ID; publish; sign; DMG; fastlane notarize with API key; **upload only if notarize succeeds**; missing secrets in notarize mode → fail before upload.
- **osx-x64 / macos-13**: out of scope for this feature (deferred).
- **ubuntu**: linux-x64 tar.gz (existing publish-preview path).
- Optional draft prerelease aggregation job.

**Rationale**: Fail-closed (grill Q3-A); ASC API key (grill Q4-B).

**Alternatives considered**: Upload ad-hoc on failure with label — rejected.

## R6 — Relationship to existing scripts

**Decision**: Keep `scripts/ci/import-apple-signing.sh` for keychain import. Refactor `package-macos-internal.sh` to stop calling `notarytool` directly for the primary path; invoke `fastlane notarize_*` instead. `publish-preview.ps1` focuses on linux (+ optional non-primary zips); Windows MSI goes through `package-windows-msi.ps1` + WiX project.

**Rationale**: Spec assumes replace/wrap so fastlane is source of truth.

## R7 — Bundle / product identity and macOS arch

**Decision**:
- macOS bundle id remains `com.servicebusexplorer.internal` until a public rename is separately decided (pass to fastlane `bundle_id`).
- MSI ProductName: `Service Bus Explorer` (Preview); stable **UpgradeCode** GUID committed in WiX; ProductVersion from `src/App/App.csproj` `<Version>` (numeric MSI version = major.minor.build from that).
- First CI ships **osx-arm64 only**; **osx-x64 deferred** (clarify 2026-08-02: B).

**Rationale**: Continuity with current macOS script; upgrade path for preview builds; Intel Mac platform support is winding down.

**Alternatives considered**: Both arch from day one — rejected for this feature; universal binary — deferred.

## R8 — Linux

**Decision**: Continue `linux-x64.tar.gz` only; document Secret Service in preview-installation.md.

**Rationale**: FR-010 / US4.

## Resolved clarifications

All grill items (Q1–Q7) are encoded in spec Assumptions/FRs; no remaining `NEEDS CLARIFICATION` in Technical Context.
