# Contract: Artifact Manifest

**Feature**: `002-preview-installer-packaging`

## MANIFEST.txt (required beside artifacts)

Line-oriented `key=value`, UTF-8, no secrets.

```text
product=Service Bus Explorer
version=1.0.1-internal.1
preview=true
artifact.windows.x64=ServiceBusExplorer-1.0.1-internal.1-win-x64.msi
sha256.windows.x64=<hex>
signing.windows.x64=unsigned
notarization.windows.x64=n/a
artifact.macos.arm64=ServiceBusExplorer-1.0.1-internal.1-osx-arm64.dmg
sha256.macos.arm64=<hex>
signing.macos.arm64=developer-id
notarization.macos.arm64=notarized
artifact.linux.x64=ServiceBusExplorer-1.0.1-internal.1-linux-x64.tar.gz
sha256.linux.x64=<hex>
signing.linux.x64=unsigned
notarization.linux.x64=n/a
```

*(No `artifact.macos.x64` keys required for this feature; osx-x64 deferred.)*
## Sidecar checksums

Each binary artifact `F` MUST have `F.sha256` containing:

```text
<hex>  <basename>
```

verifiable with `sha256sum -c` / `Get-FileHash` / `shasum -a 256 -c`.

## Invariants

1. If `notarization.macos.*=notarized` then corresponding artifact was uploaded only after staple success.
2. Windows keys MUST use `signing.*=unsigned` for this feature.
3. Missing macOS keys on a “full preview” release is allowed only if that arch job was explicitly skipped (document in workflow); notarize failure MUST omit upload entirely for that arch.
