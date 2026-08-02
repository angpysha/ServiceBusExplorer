# Preview installation guide

Canonical install steps for **preview / internal** Avalonia (`.NET 10`) packages produced by
feature `002-preview-installer-packaging`.

| Platform | Artifact | Script / job |
|----------|----------|--------------|
| Windows 10/11 **x64** | Unsigned `.msi` | `pwsh ./scripts/package-windows-msi.ps1` |
| macOS 13+ **Apple Silicon** | Notarized `.dmg` | `./scripts/package-macos-internal.sh` + fastlane |
| Ubuntu 22.04+ **x64** | `.tar.gz` archive | `pwsh ./scripts/publish-preview.ps1 -Rids linux-x64` |

Checksums: every binary has a matching `.sha256` sidecar. Metadata lives in `MANIFEST.txt`
([artifact-manifest contract](../specs/002-preview-installer-packaging/contracts/artifact-manifest.md)).

---

## Windows (win-x64 MSI)

### Build

Requires a **Windows** host with .NET 10 SDK and WiX Toolset SDK 4+ (GitHub `windows-2022` is fine).

```powershell
pwsh ./scripts/package-windows-msi.ps1
# Expect: artifacts/windows/ServiceBusExplorer-<version>-win-x64.msi (+ .sha256 + MANIFEST.txt)
```

MANIFEST must show `signing.windows.x64=unsigned`.

### SmartScreen / unsigned publisher warning

Preview MSIs are **not** Authenticode-signed in this feature. Windows may show SmartScreen /
“Unknown publisher” prompts. That is expected for internal evaluation builds. Prefer
downloading from the project’s GitHub Actions artifacts or draft prerelease you trust.
Authenticode is explicitly out of scope for feature 002.

### Install / uninstall checklist (VM validation)

CI cannot always elevate an interactive MSI UI. Use a Windows 10 22H2+ / Windows 11 **x64** VM:

#### Per-user (default — no admin)

1. Double-click the MSI (or `msiexec /i ServiceBusExplorer-…-win-x64.msi`).
2. Choose **per-user** / current user when prompted (`WixUI_Advanced`; default is per-user).
3. Confirm Start Menu shortcut **Service Bus Explorer** appears for the current user.
4. Launch the app; connect to a test namespace if available.
5. Uninstall: **Settings → Apps → Installed apps → Service Bus Explorer → Uninstall**.
6. Confirm shortcut and install folder are gone for that user.

#### Per-machine (elevated)

1. Run the MSI elevated, or:  
   `msiexec /i ServiceBusExplorer-…-win-x64.msi MSIINSTALLPERUSER="" ALLUSERS=2`
2. Accept UAC; choose **all users** / per-machine scope in the wizard when applicable.
3. Confirm machine-wide Start Menu / Program Files install.
4. Uninstall via **Settings → Apps** (or `msiexec /x {ProductCode}`) and confirm removal.

Dual-purpose authoring: WiX `Scope="perUserOrMachine"` (`ALLUSERS=2`, per-user default).

---

## macOS (osx-arm64 DMG) — Apple Silicon only

**This feature ships Apple Silicon (`osx-arm64`) only.** Intel (`osx-x64`) DMGs are deferred.

### Build + notarize

```bash
# CI: import Developer ID + ASC API key (no provisioning profile)
./scripts/ci/import-apple-signing.sh

# Local/CI package: publish → Developer ID sign → DMG → fastlane notarize
RID=osx-arm64 NOTARIZE=1 SKIP_LAUNCH_SMOKE=1 ./scripts/package-macos-internal.sh
```

Order is mandatory: **sign → DMG → notarize** (never notarize an unsigned release DMG).
Notarization uses fastlane [`notarize`](https://docs.fastlane.tools/actions/notarize/) with an
App Store Connect API key. If `NOTARIZE=1` and cert/API key material is missing, the script
**fails closed** and CI must **not** upload macOS artifacts.

### Install (Gatekeeper)

1. Open the `.dmg` and drag **Service Bus Explorer** to **Applications**.
2. On a notarized build, launch normally:

```bash
spctl --assess --type execute --verbose=4 "/Applications/Service Bus Explorer.app"
open "/Applications/Service Bus Explorer.app"
```

3. `stapler validate` on the DMG should succeed for notarized builds.
4. Ad-hoc / unnotarized local builds may require Finder **Open** or quarantine removal — those
   are **not** the evaluator primary path.

MANIFEST keys on success: `signing.macos.arm64=developer-id`, `notarization.macos.arm64=notarized`.

Secrets (names only): see [`packaging/README.md`](../packaging/README.md) and
[`contracts/github-secrets.md`](../specs/002-preview-installer-packaging/contracts/github-secrets.md).

---

## Linux (linux-x64 archive)

```powershell
pwsh ./scripts/publish-preview.ps1 -Rids linux-x64
```

Extract on Ubuntu 22.04+:

```bash
tar -xzf ServiceBusExplorer-*-linux-x64.tar.gz
./ServiceBusExplorer
```

### Secret Service / libsecret

Credential storage on Linux expects a working **Secret Service** (e.g. GNOME Keyring /
`libsecret`). Ensure a user session with Secret Service is available before testing vault
store/retrieve. Headless agents may need a session bus + keyring unlocked.

---

## Maintainer CI

Workflow: [`.github/workflows/preview-packages.yml`](../.github/workflows/preview-packages.yml)

Prefer GitHub Environment `macos-notarize` with required reviewers. Notarize mode without
secrets must fail the macOS job with **no** macOS artifact upload.
