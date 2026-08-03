# fastlane — optional local macOS notarization helper

CI notarization is driven by [`scripts/package-macos-internal.sh`](../scripts/package-macos-internal.sh)
using **`xcrun notarytool` + `stapler`** (fail-closed). The fastlane `notarize` action is
**not** used — it has crashed parsing notarytool JSON even when Apple returned `Accepted`.

This Fastfile lane remains as an optional local wrapper around the same Apple tooling.

Order of operations (never reverse for release):

1. `dotnet publish` `osx-arm64` (Apple Silicon only for this feature)
2. Build `.app` + Developer ID codesign (hardened runtime + `src/App/Entitlements.plist`)
3. Create UDZO `.dmg`
4. `notarytool submit --wait` → require `Accepted` → `stapler staple` + `validate`

Orchestrator: [`scripts/package-macos-internal.sh`](../scripts/package-macos-internal.sh)  
Cert import: [`scripts/ci/import-apple-signing.sh`](../scripts/ci/import-apple-signing.sh)

## Secrets (names only)

Mirrored from [`contracts/github-secrets.md`](../specs/002-preview-installer-packaging/contracts/github-secrets.md):

| Secret / env | Purpose |
|--------------|---------|
| `MACOS_CERTIFICATE_BASE64` | Developer ID Application `.p12` (base64) |
| `MACOS_CERTIFICATE_PWD` | `.p12` password |
| `KEYCHAIN_PASSWORD` | Ephemeral CI keychain password |
| `APP_STORE_CONNECT_API_KEY_ID` | ASC API Key ID |
| `APP_STORE_CONNECT_ISSUER_ID` | ASC Issuer ID |
| `APP_STORE_CONNECT_API_KEY_P8_BASE64` | `.p8` private key (base64) |

Optional laptop fallback (not required for CI notarize): `APPLE_ID`, `NOTARY_TOOL_PASSWORD`, `APPLE_TEAM_ID`.

Do **not** commit secret values, `.p12`, or `.p8` files. Prefer Environment `macos-notarize`.

## Local lane (optional)

After `import-apple-signing.sh` (exports `APP_STORE_CONNECT_API_KEY_P8_PATH` + id/issuer):

```bash
fastlane macos_notarize_dmg dmg_path:artifacts/macos-internal/ServiceBusExplorer-….dmg
```

Prefer a full package run for CI parity:

```bash
NOTARIZE=1 ./scripts/package-macos-internal.sh
```

Live notarize smoke is gated by `SBE_NOTARIZE=1` / `NOTARIZE=1`.
