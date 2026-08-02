# TEST-002 — Preview installer packaging quickstart evidence

**Feature**: `002-preview-installer-packaging`  
**Date**: 2026-08-02  
**Host**: macOS (Apple Silicon developer machine)  
**Sanitized**: no secrets, no certificate material, no connection strings

## Checklist (from `specs/002-preview-installer-packaging/quickstart.md`)

| Step | Result | Notes |
|------|--------|-------|
| §1 Windows MSI (`package-windows-msi.ps1`) | **BLOCKED** | Script correctly refuses non-Windows host: requires `windows-2022` / local Windows + WiX SDK. Authoring present (`packaging/windows/*`); smoke tests assert UpgradeCode + `Scope=perUserOrMachine` + unsigned MANIFEST contract. |
| §1 Install/uninstall VM | **NOT RUN** | Needs Windows 10/11 x64 VM after MSI build in CI. |
| §2 macOS DMG + notarize | **BLOCKED (secrets)** | Live `NOTARIZE=1` not executed — ASC API key / Developer ID secrets not available in this session. Script + Fastfile + fail-closed workflow gate verified by tests. |
| §2 Gatekeeper / stapler | **NOT RUN** | Depends on notarized DMG from §2. |
| §3 Linux archive | **PASS** | `pwsh ./scripts/publish-preview.ps1 -Rids linux-x64` produced `artifacts/preview/ServiceBusExplorer-1.0.1-linux-x64.tar.gz` + `.sha256` + `MANIFEST.txt` with `signing.linux.x64=unsigned`. PackageSmoke linux case passed. |
| §4 Full pipeline (GHA) | **NOT RUN** | Workflow rewritten; trigger manually after secrets + Environment `macos-notarize` are configured. |

## Automated packaging tests (this host)

```text
CiWorkflowContract.Tests.ps1     PASS
MacOsFailClosed.Tests.ps1        PASS
WindowsMsiSmoke.Tests.ps1        PASS (authoring; MSI binary SKIP)
MacOsPackageSmoke.Tests.ps1      PASS (live notarize SKIP)
PackageSmoke.Tests.ps1           PASS (linux PASS; win/mac SKIP)
ManifestSchema.Tests.ps1         (prior session PASS)
```

## Workflow contract assertions (sanitized)

- Jobs: `windows-msi` → `package-windows-msi.ps1`; `linux-archive` → `publish-preview.ps1 -Rids linux-x64`; `macos-dmg` → `import-apple-signing.sh` + `package-macos-internal.sh` with `NOTARIZE=1`, `RID=osx-arm64`.
- Runners: `windows-2022`, `ubuntu-22.04`, `macos-14` only (no `macos-13` / `osx-x64`).
- Fail-closed: missing any of the six contract secrets fails the macOS job before package; upload lists `*.dmg` (+ sha256 + MANIFEST) only — **no** `*.zip`.
- Environment: `macos-notarize`.

## Blockers for full T032 / quickstart green

1. **WiX / Windows**: MSI build and install/uninstall evidence require a Windows host or successful `windows-msi` GHA job.
2. **Apple secrets**: Notarized DMG evidence requires repository secrets from `contracts/github-secrets.md` (and preferably Environment `macos-notarize`).
3. **Draft release**: optional; needs at least Windows + Linux artifacts (and macOS when notarize enabled).

## How to finish evidence in CI

1. Configure the six Apple secrets (names only in contract — never commit values).
2. Create Environment `macos-notarize` (optional reviewers).
3. **Actions → preview-packages → Run workflow** with `notarize=true`.
4. Download MSI / DMG / tar.gz artifacts; verify MANIFEST keys and checksums; run VM install checklist from `docs/preview-installation.md`.
