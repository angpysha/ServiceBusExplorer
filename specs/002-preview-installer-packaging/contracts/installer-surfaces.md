# Contract: Installer Surfaces

**Feature**: `002-preview-installer-packaging`

## Windows MSI (win-x64)

| Surface | Contract |
|---------|----------|
| Format | `.msi` built with WiX Toolset v4+ |
| Payload | Self-contained Avalonia publish (`win-x64`) |
| Scope | Dual-purpose: per-user (default) and per-machine via wizard / `MSIINSTALLPERUSER` + `ALLUSERS=2` |
| Elevation | Per-user: no admin; per-machine: UAC |
| ARP | Product name, version, publisher visible; uninstall removes app |
| Signing | Unsigned; docs warn SmartScreen |
| CLI check | `msiexec /i …` and `/x` documented for automation tests |

## macOS DMG (osx-arm64)

| Surface | Contract |
|---------|----------|
| Format | UDZO `.dmg` containing `Service Bus Explorer.app` + Applications symlink |
| Arch | **osx-arm64** only for this feature (`osx-x64` deferred) |
| Bundle ID | `com.servicebusexplorer.internal` (until renamed) |
| Min OS | macOS 13.0 |
| Signing | Developer ID Application + hardened runtime + Entitlements.plist |
| Notarization | fastlane `notarize`; ASC API Key in CI; stapled |
| Primary | DMG is evaluator primary; zip optional |
| Gatekeeper | Fresh Mac launch without `xattr` quarantine removal |

## Linux archive (linux-x64)

| Surface | Contract |
|---------|----------|
| Format | `.tar.gz` of self-contained publish folder |
| Signing | unsigned |
| Docs | Secret Service / libsecret prerequisites |

## Out of contract

Legacy WinForms EXE/Chocolatey nupkg; Mac App Store; MSIX; win-arm64 MSI; Authenticode.
