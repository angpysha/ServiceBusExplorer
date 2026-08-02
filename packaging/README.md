# Packaging — Preview Installers

This directory holds installer authoring for **preview / internal** builds of the Avalonia
(`net10`) Service Bus Explorer app.

| Path | Purpose |
|------|---------|
| `windows/` | WiX v4+ MSI project (`ServiceBusExplorer.wixproj`, `Package.wxs`) |
| `../fastlane/` | fastlane `notarize` lane for macOS DMG |
| `../scripts/package-windows-msi.ps1` | Windows MSI orchestration |
| `../scripts/package-macos-internal.sh` | macOS `.app` → DMG → fastlane notarize |
| `../scripts/publish-preview.ps1` | Linux (and demoted zip) archives |
| `../tests/Packaging/` | Manifest schema + package / CI contract smoke tests |

Canonical feature docs: [`specs/002-preview-installer-packaging/`](../specs/002-preview-installer-packaging/).

## Artifact outputs

| OS | Artifact | Notes |
|----|----------|-------|
| Windows x64 | `artifacts/windows/*.msi` | Unsigned Authenticode (SmartScreen warning) |
| macOS arm64 | `artifacts/macos-internal/*.dmg` | Developer ID + notarized/stapled via fastlane |
| Linux x64 | `artifacts/preview/*-linux-x64.tar.gz` | Unsigned archive |

Each binary has a `.sha256` sidecar and keys in `MANIFEST.txt` per
[`contracts/artifact-manifest.md`](../specs/002-preview-installer-packaging/contracts/artifact-manifest.md).

macOS **zip** archives are demoted (not uploaded by CI). Evaluator primary is the **DMG**.

## Maintainer runbook (GitHub Actions)

Workflow: [`.github/workflows/preview-packages.yml`](../.github/workflows/preview-packages.yml)

### Trigger

1. Ensure repository secrets below are set (Settings → Secrets and variables → Actions).
2. Prefer attaching the macOS job to Environment **`macos-notarize`** with required reviewers.
3. **Actions → preview-packages → Run workflow**
   - `notarize` = **true** (default): builds notarized osx-arm64 DMG; **fails closed** if any Apple secret is missing (**no** macOS artifact upload).
   - `notarize` = **false**: skips the macOS job (Windows MSI + Linux tar.gz still run).
   - `create_release` = optional draft GitHub prerelease.
4. Or push a tag matching `v*-preview*` / `*-internal.*` (macOS notarize always required on tags).

### Jobs

| Job | Runner | Script |
|-----|--------|--------|
| `windows-msi` | `windows-2022` | `scripts/package-windows-msi.ps1` |
| `linux-archive` | `ubuntu-22.04` | `scripts/publish-preview.ps1 -Rids linux-x64` |
| `macos-dmg` | `macos-14` | `scripts/ci/import-apple-signing.sh` → `package-macos-internal.sh` (`NOTARIZE=1`, `RID=osx-arm64`) + fastlane |
| `draft-release` | `ubuntu-latest` | optional; needs Windows + Linux success; macOS success or skipped |

### GitHub Actions secrets (names only — never commit values)

Contract: [`contracts/github-secrets.md`](../specs/002-preview-installer-packaging/contracts/github-secrets.md).

#### Required for notarize-enabled macOS jobs

| Secret | Purpose |
|--------|---------|
| `MACOS_CERTIFICATE_BASE64` | Base64-encoded Developer ID Application `.p12` |
| `MACOS_CERTIFICATE_PWD` | Password for the `.p12` |
| `KEYCHAIN_PASSWORD` | Temporary CI keychain password |
| `APP_STORE_CONNECT_API_KEY_ID` | ASC API Key ID |
| `APP_STORE_CONNECT_ISSUER_ID` | ASC Issuer ID |
| `APP_STORE_CONNECT_API_KEY_P8_BASE64` | Base64 of the `.p8` private key |

#### Behavior

- **Notarize mode ON** and any required secret missing → job **fails**; **no** macOS artifact upload (fail-closed).
- Prefer GitHub Environment `macos-notarize` with required reviewers.
- Windows / Linux jobs need **no** Apple or Authenticode secrets for this feature.
- No Mac App Store provisioning profile.
- Optional laptop-only fallback (`APPLE_ID` / `NOTARY_TOOL_PASSWORD` / `APPLE_TEAM_ID`) is **not** required for CI.

### Local smoke (without CI)

```bash
# Linux archive (any host with .NET 10)
pwsh ./scripts/publish-preview.ps1 -Rids linux-x64

# Windows MSI (Windows host + WiX SDK via NuGet)
pwsh ./scripts/package-windows-msi.ps1

# macOS DMG (Apple Silicon + Developer ID; notarize with secrets)
./scripts/ci/import-apple-signing.sh   # CI-style; or local keychain identity
RID=osx-arm64 NOTARIZE=1 ./scripts/package-macos-internal.sh

# Contract / packaging tests
pwsh ./tests/Packaging/CiWorkflowContract.Tests.ps1
pwsh ./tests/Packaging/PackageSmoke.Tests.ps1
```

See also [`docs/preview-installation.md`](../docs/preview-installation.md) and
[`specs/002-preview-installer-packaging/quickstart.md`](../specs/002-preview-installer-packaging/quickstart.md).
