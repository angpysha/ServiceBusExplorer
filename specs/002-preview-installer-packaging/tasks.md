# Tasks: Preview Installer Packaging

**Input**: Design documents from `specs/002-preview-installer-packaging/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: Packaging smoke and manifest schema checks are required (plan + constitution IV). MSI
install/uninstall may be documented VM steps where GHA cannot elevate interactively; automate
what CI can (build MSI, validate tables/properties, checksums, fail-closed upload gates).

**Organization**: Phases follow Setup → Foundational → US1 (Windows MSI) → US2 (macOS DMG) →
US3 (CI) → US4 (Linux) → Polish. US1 and US2 may proceed in parallel after Foundational.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: May run concurrently when listed files are disjoint
- **[Story]**: Maps to `spec.md` user stories (US1–US4)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create packaging directories and shared helpers without shipping installers yet

- [ ] T001 Create packaging layout `packaging/windows/`, `packaging/README.md`, `fastlane/`, and `tests/Packaging/` per `plan.md`
- [ ] T002 [P] Add shared version/RID helpers used by packaging scripts in `scripts/lib/PackagingCommon.ps1` (read version from `src/App/App.csproj`)
- [ ] T003 [P] Document secret names (no secret values) by mirroring `specs/002-preview-installer-packaging/contracts/github-secrets.md` into `packaging/README.md` and `fastlane/README.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Manifest contract + smoke harness that all OS stories must satisfy

**⚠️ CRITICAL**: No US installer work merges without these checks existing (may start red)

- [ ] T004 Implement MANIFEST writer/reader aligned with `specs/002-preview-installer-packaging/contracts/artifact-manifest.md` in `scripts/lib/Write-PreviewManifest.ps1`
- [ ] T005 [P] Write failing then passing manifest schema tests in `tests/Packaging/ManifestSchema.Tests.ps1`
- [ ] T006 [P] Scaffold packaging smoke entrypoint (checksum + required keys) in `tests/Packaging/PackageSmoke.Tests.ps1`
- [ ] T007 Align `scripts/publish-preview.ps1` to emit linux-focused archives and call shared manifest helpers without claiming macOS DMG/MSI as primary outputs

**Checkpoint**: Manifest/smoke harness ready; OS stories can proceed

---

## Phase 3: User Story 1 — Windows MSI (Priority: P1) 🎯 MVP

**Goal**: win-x64 dual-purpose (per-user default + per-machine) unsigned MSI via WiX v4+

**Independent Test**: Run `pwsh ./scripts/package-windows-msi.ps1`; verify MSI exists + `.sha256`;
install per-user and per-machine on a Windows 10/11 VM; uninstall via Apps; MANIFEST shows
`signing.windows.x64=unsigned`

### Tests for User Story 1

- [ ] T008 [P] [US1] Write failing WiX dual-scope / product-identity assertions first in `tests/Packaging/WindowsMsiSmoke.Tests.ps1` (UpgradeCode present, ALLUSERS/MSIINSTALLPERUSER expectations, unsigned label)
- [ ] T009 [P] [US1] Document interactive install/uninstall checklist for per-user and per-machine in `docs/preview-installation.md` (Windows section) for VM validation when CI cannot elevate UI

### Implementation for User Story 1

- [ ] T010 [US1] Author WiX v4+ project `packaging/windows/ServiceBusExplorer.wixproj` and dual-purpose `packaging/windows/Package.wxs` (`ALLUSERS=2`, per-user default, harvest self-contained publish)
- [ ] T011 [US1] Implement `scripts/package-windows-msi.ps1` (`dotnet publish -r win-x64 --self-contained` → WiX build → MSI + sha256 + MANIFEST fragment under `artifacts/windows/`)
- [ ] T012 [US1] Wire smoke tests to built MSI metadata and make `tests/Packaging/WindowsMsiSmoke.Tests.ps1` + `PackageSmoke.Tests.ps1` pass for Windows artifact keys
- [ ] T013 [US1] State SmartScreen / unsigned publisher warnings in `docs/preview-installation.md` and `README.md` (Windows preview MSI)

**Checkpoint**: US1 independently deliverable (local/CI Windows artifact)

---

## Phase 4: User Story 2 — macOS notarized DMG (Priority: P1)

**Goal**: osx-arm64 DMG: publish → Developer ID sign → DMG → fastlane notarize + staple (fail-closed)

**Independent Test**: On Apple Silicon with secrets, produce notarized DMG; `spctl --assess` /
`stapler validate`; without secrets in notarize mode job fails with no upload. Docs say Apple Silicon only.

### Tests for User Story 2

- [ ] T014 [P] [US2] Write failing then passing packaging assertions for sign-before-notarize order and arm64-only RID in `tests/Packaging/MacOsPackageSmoke.Tests.ps1` (skip live notarize unless `SBE_NOTARIZE=1`)
- [ ] T015 [P] [US2] Add fail-closed CI assertion helper (no artifact upload when notarize fails) documented/tested via workflow job logic comments + `tests/Packaging/MacOsFailClosed.Tests.ps1` where feasible

### Implementation for User Story 2

- [ ] T016 [US2] Add `fastlane/Fastfile` lane that calls fastlane [`notarize`](https://docs.fastlane.tools/actions/notarize/) with ASC API key inputs and stapling for the DMG/`bundle_id` `com.servicebusexplorer.internal`
- [ ] T017 [US2] Refactor `scripts/package-macos-internal.sh` to: `dotnet publish` osx-arm64 → `.app` → Developer ID codesign (`src/App/Entitlements.plist`) → DMG → invoke fastlane notarize (remove primary `notarytool`-only path); default RID `osx-arm64` only
- [ ] T018 [US2] Keep/adjust `scripts/ci/import-apple-signing.sh` for `.p12` import and ephemeral `.p8`/API key material for fastlane (no provisioning profile)
- [ ] T019 [US2] Emit macOS MANIFEST keys (`signing.macos.arm64=developer-id`, `notarization.macos.arm64=notarized`) + sha256 under `artifacts/macos-internal/`
- [ ] T020 [US2] Document Apple Silicon–only + Gatekeeper/notarized install steps in `docs/preview-installation.md` and `README.md`

**Checkpoint**: US2 independently deliverable on Apple Silicon with secrets

---

## Phase 5: User Story 3 — Maintainer GitHub Actions (Priority: P2)

**Goal**: One workflow builds Windows MSI + macOS notarized DMG (+ linux when US4 ready) with secrets and fail-closed macOS upload

**Independent Test**: `workflow_dispatch` on `preview-packages.yml` with secrets → artifacts; notarize mode without secrets → macOS job fails, no macOS upload

### Tests for User Story 3

- [ ] T021 [P] [US3] Add workflow-level expectations checklist in `tests/Packaging/CiWorkflowContract.Tests.ps1` (or documented assert script) verifying fail-closed upload conditions and required secret names from `contracts/github-secrets.md`

### Implementation for User Story 3

- [ ] T022 [US3] Rewrite `.github/workflows/preview-packages.yml`: windows-2022 MSI job; macos-14 osx-arm64 fastlane notarize job (fail-closed, no macos-13/x64); optional draft release; remove ad-hoc-as-notarized path
- [ ] T023 [US3] Ensure macOS job uses `scripts/ci/import-apple-signing.sh` + `package-macos-internal.sh` / fastlane; Windows job uses `scripts/package-windows-msi.ps1`
- [ ] T024 [US3] Document maintainer runbook (secrets table, Environment recommendation) in `packaging/README.md` and link from `README.md`

**Checkpoint**: Maintainer can produce preview installers from Actions alone

---

## Phase 6: User Story 4 — Linux archive (Priority: P3)

**Goal**: linux-x64 tar.gz in the same preview pipeline with Secret Service notes

**Independent Test**: `pwsh ./scripts/publish-preview.ps1 -Rids linux-x64`; extract on Ubuntu 22.04+; docs list Secret Service prerequisites

### Tests for User Story 4

- [ ] T025 [P] [US4] Extend `tests/Packaging/PackageSmoke.Tests.ps1` for linux artifact + sha256 + MANIFEST keys

### Implementation for User Story 4

- [ ] T026 [US4] Ensure `scripts/publish-preview.ps1` produces `artifacts/preview/*-linux-x64.tar.gz` + sidecar and MANIFEST keys per contract
- [ ] T027 [US4] Add ubuntu job (or matrix entry) in `.github/workflows/preview-packages.yml` uploading linux artifact
- [ ] T028 [US4] Document Linux extract/launch + Secret Service prerequisites in `docs/preview-installation.md`

**Checkpoint**: Linux archive ships alongside MSI/DMG

---

## Phase 7: Polish & Cross-Cutting

**Purpose**: Docs consistency, deprecate misleading paths, quickstart evidence

- [ ] T029 [P] Expand `docs/preview-installation.md` as canonical FR-008 guide (Windows MSI dual scope, macOS Apple Silicon + notarized DMG, Linux archive)
- [ ] T030 [P] Update `README.md` First-internal / preview packaging sections to point at MSI/DMG/fastlane and Apple Silicon–only disclaimer (remove “unsigned zip is the evaluator primary” messaging where superseded)
- [ ] T031 Remove or clearly demote non-primary macOS zip-as-release claims in `scripts/publish-preview.ps1` / workflow artifact lists
- [ ] T032 Run `specs/002-preview-installer-packaging/quickstart.md` validation checklist and record sanitized evidence under `docs/sdlc/test-reports/` (or note blockers)
- [ ] T033 [P] Sync beads issues for T001–T032 under epic for `002-preview-installer-packaging` (or link from existing packaging beads)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: start immediately
- **Foundational (Phase 2)**: after Setup — blocks US stories
- **US1 (Phase 3)** & **US2 (Phase 4)**: after Foundational; **parallel OK**
- **US3 (Phase 5)**: after US1 + US2 (needs both scripts green)
- **US4 (Phase 6)**: after Foundational; can parallel with US1/US2; wire into US3 workflow when ready
- **Polish (Phase 7)**: after desired stories

### User Story Dependencies

```text
Setup → Foundational → US1 (MSI) ──┐
                    ↘ US2 (DMG) ───┼→ US3 (CI) → Polish
                    ↘ US4 (Linux) ─┘ (US4 may merge into US3 earlier)
```

### Parallel Opportunities

- T002 ∥ T003 (Setup)
- T005 ∥ T006 (Foundational tests)
- After Foundational: entire US1 ∥ entire US2
- T008 ∥ T009; T014 ∥ T015; T025 alone
- T029 ∥ T030 ∥ T033 (Polish)

---

## Parallel Example: After Foundational

```bash
# Developer A — Windows MSI
Task: T008–T013 in packaging/windows/, scripts/package-windows-msi.ps1, tests/Packaging/WindowsMsiSmoke.Tests.ps1

# Developer B — macOS DMG + fastlane
Task: T014–T020 in fastlane/, scripts/package-macos-internal.sh, tests/Packaging/MacOsPackageSmoke.Tests.ps1
```

---

## Implementation Strategy

### MVP First (US1 only)

1. Phase 1–2  
2. Phase 3 US1 (Windows MSI)  
3. **STOP**: validate MSI install/uninstall on a VM  
4. Then US2 (needs Apple secrets) → US3 → US4 → Polish  

### Incremental Delivery

1. MSI preview for Windows evaluators  
2. Notarized arm64 DMG for Mac evaluators  
3. Unified Actions workflow  
4. Linux archive + docs polish  

---

## Notes

- No Mac App Store provisioning profile; credentials = `.p12` + ASC `.p8` (+ IDs)
- Order: **sign → DMG → notarize** (never unsigned→notarize as release path)
- osx-x64 / win-arm64 / Authenticode explicitly out of scope
- Existing `preview-packages.yml` / `package-macos-internal.sh` are evolve-in-place targets
- Format: every task has checkbox, ID, optional [P], story label where required, and file paths
