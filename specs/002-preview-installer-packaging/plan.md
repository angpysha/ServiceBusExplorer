# Implementation Plan: Preview Installer Packaging

**Branch**: `feature/avalonia-servicebus-mvp` (feature dir `002-preview-installer-packaging`) | **Date**: 2026-08-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/002-preview-installer-packaging/spec.md` (grill resolutions applied)

## Summary

Deliver evaluator-facing installers for the Avalonia (.NET 10) app: a **WiX v4+ win-x64 MSI** with dual per-user/per-machine scope (unsigned Authenticode deferred), a **notarized/stapled macOS DMG** whose notarization is driven by **fastlane `notarize`** + App Store Connect API Key in CI (fail-closed), and keep **linux-x64 tar.gz** as an archive. Replace or wrap the existing shell-centric notarize path so fastlane is the CI source of truth; document secrets, manifests, and install steps.

## Technical Context

**Language/Version**: C# / .NET 10 (app publish); WiX v4+ (MSI authoring); Ruby/fastlane (macOS notarize lane); Bash/PowerShell (orchestration)

**Primary Dependencies**: `dotnet publish` self-contained; WiX Toolset 4+; fastlane + `notarize` action; Apple Developer ID Application certificate; App Store Connect API Key; existing Avalonia `src/App` + `Entitlements.plist`

**Storage**: N/A (artifacts + checksums + MANIFEST only; no app data model change)

**Testing**: Packaging smoke scripts (PowerShell); MSI install/uninstall on Windows CI or documented VM; macOS `codesign`/`spctl`/staple checks after notarize; checksum verification; fail-closed job assertions (no artifact upload when notarize fails)

**Target Platform**: Windows 10 22H2+ **x64** (MSI); macOS 13+ **arm64** DMG (osx-x64 deferred); Ubuntu 22.04+ x64 (tar.gz)

**Project Type**: Desktop app packaging / CI release engineering (no new in-app product features)

**Performance Goals**: Packaging jobs complete within typical GHA macOS notarization windows (often minutes; tolerate Apple queue); MSI build &lt; 15 min on windows-2022

**Constraints**: No secrets in git; notarize-enabled CI fail-closed; no Authenticode this feature; no win-arm64 MSI; Dual-purpose MSI per Microsoft single-package authoring (`ALLUSERS=2`); fastlane primary for notarize (not ad-hoc `notarytool` as marketed path)

**Scale/Scope**: Three OS artifact families per preview version; one GitHub Actions workflow entrypoint; docs under `docs/preview-installation.md` (+ README pointers)

## Constitution Check

*GATE: evaluated before Phase 0 and re-evaluated after Phase 1.*

### Pre-design evaluation

| Principle | Result | Design response |
|---|---|---|
| I. Avalonia is the product UI | PASS | Packages `src/App` only; no WinForms installer work. |
| II. Preserve layer boundaries | PASS | No Core/ViewModels/Services changes required; packaging lives under `packaging/`, `fastlane/`, `scripts/`, `.github/workflows/`. |
| III. Secure modern Azure integration | PASS | No Azure auth changes; Apple/GHA secrets only; no credentials in artifacts. |
| IV. Tests define completion | PASS | Package smoke + install/uninstall + notarize status checks gate completion. |
| V. Async, observable, resilient | PASS | N/A for installer UX; CI fails closed on notarize errors. |
| Technical/security constraints | PASS | Justified new deps: WiX, fastlane (packaging-only). |
| Workflow/governance | PASS | Spec/plan/tasks via Spec Kit; beads for implementation tracking. |

No constitutional exception required.

### Post-design evaluation

| Principle | Result | Notes |
|---|---|---|
| I–V + constraints | PASS | Contracts describe artifact/manifest/secret surfaces only; app layers untouched. |
| Complexity | PASS | Dual-scope MSI + fastlane are required by spec; shell wrappers retained only as thin orchestrators. |

## Project Structure

### Documentation (this feature)

```text
specs/002-preview-installer-packaging/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── artifact-manifest.md
│   ├── github-secrets.md
│   └── installer-surfaces.md
└── tasks.md                 # /speckit-tasks (not this command)
```

### Source Code (repository root)

```text
src/App/                          # published binary (existing)
src/App/Entitlements.plist        # hardened runtime (existing; keep)

packaging/
├── windows/
│   ├── ServiceBusExplorer.wixproj
│   ├── Package.wxs               # dual-purpose MSI (WiX v4+)
│   └── ...
└── README.md

fastlane/
├── Fastfile                      # lane: build_app / notarize_dmg
├── Appfile                       # optional; prefer CI env
└── README.md

scripts/
├── publish-preview.ps1           # evolve: win MSI + linux tar; defer raw osx zip as primary
├── package-macos-internal.sh     # evolve: publish+sign+dmg; call fastlane notarize
├── ci/import-apple-signing.sh    # keep: import Developer ID .p12
└── package-windows-msi.ps1       # new: dotnet publish + WiX build

.github/workflows/
└── preview-packages.yml          # evolve: WiX job; macOS fastlane; fail-closed upload

docs/
└── preview-installation.md       # new/expand per FR-008

tests/Packaging/
├── PackageSmoke.Tests.ps1
└── ManifestSchema.Tests.ps1
```

**Structure Decision**: Packaging and CI assets at repo root (`packaging/`, `fastlane/`, `scripts/`, workflow). No changes to Avalonia app architecture. Supersedes installer-format intent of 001 T029 zips for Windows/macOS evaluator delivery while retaining linux tar.gz.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Dual-purpose MSI (per-user + per-machine) | Spec grill Q2 = both | Single scope would violate FR-003 |
| fastlane + Ruby on macOS CI | Spec FR-005 | Raw notarytool-only path rejected as primary |
| Separate osx-arm64 and osx-x64 DMGs | Deferred: arm64-only for this feature | Intel support ending; x64 later |
