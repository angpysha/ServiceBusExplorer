# Quickstart: Validate Preview Installer Packaging

**Feature**: `002-preview-installer-packaging`  
**Prerequisites**: .NET 10 SDK; Docker not required. For full macOS notarize: Apple Developer ID + ASC API Key in env/secrets. For MSI: WiX v4+ on Windows (or windows-2022 GHA).

## 1. Windows MSI (local or CI)

```powershell
pwsh ./scripts/package-windows-msi.ps1   # after implemented
# Expect: artifacts/windows/*.msi + *.sha256 + MANIFEST fragment
```

**Checks**:
- Install per-user (no admin) → app launches from Start Menu.
- Uninstall via Settings → Apps → gone from list.
- Install per-machine (elevated) → machine-wide Start Menu / Program Files as documented.
- MANIFEST shows `signing.windows.x64=unsigned`.

## 2. macOS DMG + notarize (CI preferred)

```bash
# Import cert in CI via scripts/ci/import-apple-signing.sh
# Then packaging lane (names finalized in tasks):
bundle exec fastlane macos_notarized_dmg rid:osx-arm64
```

**Checks**:
- Job fails if API key/cert missing while notarize enabled (no artifact upload).
- On success: `stapler validate` / `spctl --assess` on app from DMG (Apple Silicon host).
- MANIFEST: `signing.macos.arm64=developer-id`, `notarization.macos.arm64=notarized`.
- Docs state Apple Silicon only (no osx-x64 artifact in this feature).

## 3. Linux archive

```powershell
pwsh ./scripts/publish-preview.ps1 -Rids linux-x64
```

**Checks**: extract on Ubuntu 22.04+, binary starts (GUI session); Secret Service notes in `docs/preview-installation.md`.

## 4. Full pipeline

GitHub Actions → **preview-packages** → Run workflow with notarize enabled after secrets from [contracts/github-secrets.md](contracts/github-secrets.md) are set.

**Expected**: win MSI + macOS DMG(s) + linux tar.gz artifacts with checksums; draft release optional.

## Evidence mapping

| Spec scenario | Quickstart step |
|---------------|-----------------|
| US1 MSI install/uninstall / dual scope | §1 |
| US2 Gatekeeper / fail-closed | §2 |
| US3 maintainer CI | §4 |
| US4 Linux archive | §3 |
| FR-007 manifests | §§1–3 MANIFEST + sha256 |
