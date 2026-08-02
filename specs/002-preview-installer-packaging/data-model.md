# Data Model: Preview Installer Packaging

Logical entities for packaging metadata (not application domain models).

## PreviewArtifact

| Field | Type | Rules |
|-------|------|-------|
| filename | string | Unique within a release; includes version + rid/os |
| version | string | Matches `src/App` informational/version (e.g. `1.0.1-internal.1`) |
| os | enum | `windows` \| `macos` \| `linux` |
| arch | enum | `x64` \| `arm64` |
| format | enum | `msi` \| `dmg` \| `tar.gz` \| `zip` (zip non-primary on macOS) |
| sha256 | string | Hex digest of artifact bytes |
| signing | enum | `unsigned` \| `developer-id` \| `ad-hoc` |
| notarization | enum | `n/a` \| `notarized` \| `not notarized` \| `failed` |
| primary | bool | Evaluator-facing primary for that OS (Windows MSI, macOS DMG, Linux tar.gz) |

**Validation**: macOS primary artifact with `notarization=notarized` requires `signing=developer-id`. Windows MSI for this feature has `signing=unsigned`. `notarization=failed` artifacts MUST NOT be published to the release channel.

## ReleaseManifest

| Field | Type | Rules |
|-------|------|-------|
| version | string | Same as artifacts |
| created_utc | datetime | Build time |
| artifacts | PreviewArtifact[] | ≥1 |
| notes_ref | string | Optional path/URL to install docs |

Serialized as `MANIFEST.txt` (line-oriented key=value) and/or `MANIFEST.json` (preferred for tests). See [contracts/artifact-manifest.md](contracts/artifact-manifest.md).

## MsiProductIdentity

| Field | Type | Rules |
|-------|------|-------|
| product_name | string | Display name including Preview |
| product_version | string | MSI numeric version (e.g. `1.0.1`) |
| upgrade_code | GUID | Stable across preview builds |
| product_code | GUID | May change per build/version per WiX MajorUpgrade strategy |
| manufacturer | string | Documented publisher string |
| default_scope | enum | `per-user` (default) \| choice → `per-machine` |

## NotarizationResult

| Field | Type | Rules |
|-------|------|-------|
| submission_id | string | From Apple / fastlane log |
| status | enum | `accepted` \| `invalid` \| `rejected` \| `error` |
| package_path | string | DMG or zip submitted |
| stapled | bool | Must be true before upload when status=accepted |

**State transitions**: `pending` → `accepted` → stapled → publish; or `pending` → (`invalid`\|`rejected`\|`error`) → **fail job / no upload**.

## Relationships

```text
ReleaseManifest 1──* PreviewArtifact
NotarizationResult 0..1──1 PreviewArtifact (macos primary)
MsiProductIdentity 1──1 PreviewArtifact (windows msi)
```
