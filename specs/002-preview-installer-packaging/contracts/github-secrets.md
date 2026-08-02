# Contract: GitHub Actions Secrets (Packaging)

**Feature**: `002-preview-installer-packaging`  
**Workflow**: `.github/workflows/preview-packages.yml`

## Required for notarize-enabled macOS jobs

| Secret | Purpose |
|--------|---------|
| `MACOS_CERTIFICATE_BASE64` | Base64-encoded Developer ID Application `.p12` |
| `MACOS_CERTIFICATE_PWD` | Password for the `.p12` |
| `KEYCHAIN_PASSWORD` | Temporary CI keychain password |
| `APP_STORE_CONNECT_API_KEY_ID` | ASC API Key ID |
| `APP_STORE_CONNECT_ISSUER_ID` | ASC Issuer ID |
| `APP_STORE_CONNECT_API_KEY_P8_BASE64` | Base64 of the `.p8` private key |

Fastlane is **not** required. CI uses `xcrun notarytool` with the `.p8` + key id + issuer
exported by `scripts/ci/import-apple-signing.sh` (`APP_STORE_CONNECT_API_KEY_P8_PATH`,
`APP_STORE_CONNECT_API_KEY_ID`, `APP_STORE_CONNECT_ISSUER_ID`). An ASC API key JSON file is
also written for compatibility.

## Optional / local-only (not required for CI notarize)

| Secret / env | Purpose |
|--------------|---------|
| `APPLE_ID` + `NOTARY_TOOL_PASSWORD` + `APPLE_TEAM_ID` | Documented laptop fallback only |

## Behavior

- **Notarize mode ON** and any required secret missing → job **fails**; **no** macOS artifact upload.
- Secrets MUST NOT be echoed, committed, or written into MANIFEST/artifacts.
- Prefer GitHub Environment `macos-notarize` with required reviewers (operational hardening; not a product FR).

## Windows / Linux jobs

No Apple secrets required. No Authenticode secrets in this feature.
