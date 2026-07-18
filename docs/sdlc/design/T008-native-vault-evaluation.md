# T008 Native Vault Evaluation

**Feature**: `001-safe-servicebus-mvp` / `ServiceBusExplorer-m7n`  
**Task**: T008  
**Date**: 2026-07-18  
**Status**: Spike complete — **recommendation pending security + human approval** before T009–T011

## Purpose

Compare first-party native adapters versus a pinned maintained package for implementing
`ICredentialVault` on Windows Credential Manager, macOS Keychain Services, and Linux
freedesktop Secret Service (libsecret). Record license, maintenance, transitive dependency,
native-code, supply-chain, and fallback findings. Produce a reusable no-file-fallback
conformance harness before any production dependency is approved.

## Normative constraints (from Spec Kit)

- Production composition MUST NOT register in-memory, plaintext, DPAPI/file, encrypted-file,
  or other application-managed persistence fallbacks.
- Core stays package-neutral; adapters live under `src/App/Services/Credentials/`.
- Typed outcomes must cover Available / Unavailable / Locked / PermissionDenied /
  ProviderMissing / Unsupported / NotFound / Uncertain / Failure / Cancelled.
- Entra tokens never enter this vault.

## Candidates reviewed

### 1. `ktsu.CredentialCache` 1.2.3 — REJECTED

| Dimension | Finding |
|-----------|---------|
| License | MIT (acceptable with attribution) |
| Targets | net8.0/net9.0 on NuGet; net10.0 only via computed compatibility |
| Persistence model | README documents `PersistToStorage`, `StoragePath = "credentials.dat"`, app-managed encryption — **violates no-file-fallback** |
| Native vault docs | v1.2.3 does **not** document Credential Manager / Keychain / Secret Service |
| Transitives | Same-publisher `ktsu.AppDataStorage`, `ktsu.StrongPaths`, `ktsu.StrongStrings` |
| Maintenance | Low download volume; native rewrite landed after 1.2.3 with breaking changes |
| Supply chain | Later code P/Invokes `advapi32`, `Security.framework`, `libsecret-1.so.0` — not in the rejected 1.2.3 surface |
| Fallback | Documents in-memory / file persistence paths unsuitable for production composition |

**Decision**: Keep permanently rejected for this requirement (matches `research.md` R11).
A newer ktsu version remains a *separate* candidate only after pinned-version review + smoke.

### 2. Newer `ktsu.CredentialCache` (1.3.x native rewrite, e.g. 1.3.19) — NOT SELECTED

| Dimension | Finding |
|-----------|---------|
| Pros | Cross-platform native stores documented; MIT; NuGet shows active 1.3.x releases |
| Cons | Still documents `InMemoryCredentialStore` fallback for headless/CI; transitive ktsu stack; recent breaking API churn; must prove no file creation and disabled in-memory fallback in production composition |
| Gate | Would need pinned version, full license/transitive review, and three-OS smoke via the conformance harness |

**Decision**: Do **not** approve for T009–T011. Re-evaluate only if first-party adapters prove
unmaintainable and a pinned version passes the harness with fallbacks disabled.
`1.2.3` evidence cannot justify any newer version.

### 3. `Devlooped.CredentialManager` (Git Credential Manager store packaging) — NOT SELECTED

| Dimension | Finding |
|-----------|---------|
| Pros | Packages GCM credential stores; NS2.0; Windows/macOS/Linux |
| Cons | Tied to GCM store matrix (includes Git-built-in cache options that can require a working Git install); not shaped for opaque `CredentialReference` + typed `CredentialVaultStatus`; sponsorship/maintenance fee model; harder to guarantee no alternate store / no file-like cache under our RID smoke |
| Gate | Would need store-selection lockdown + conformance smoke proving designated OS vault only |

**Decision**: Not selected for MVP. Revisit only with explicit store pinning evidence.

### 4. Windows-only `AdysTech.CredentialManager` / Linux-only `Ace4896.DBus.Services.Secrets` / `SIL.PasswordStore` — NOT SELECTED AS PRIMARY

Platform-specific packages could wrap individual adapters, but:

- None alone cover all three required OS floors with a single reviewed dependency.
- Mixing three third-party packages multiplies license, transitive, and fallback review cost.
- Our typed failure taxonomy still needs a first-party mapping layer.

**Decision**: Prefer first-party thin adapters; optional platform helpers remain subordinate and
unapproved unless a later spike reopens them under the same gate.

### 5. First-party thin adapters — **RECOMMENDED**

Implement three App-layer adapters behind `ICredentialVault`:

| OS | Target API | Proposed type (T009–T011) |
|----|------------|---------------------------|
| Windows | Current-user Credential Manager generic credentials (`CredWrite`/`CredRead`/`CredDelete`) | `WindowsCredentialVault` |
| macOS | Login Keychain Services generic-password items | `MacOsCredentialVault` |
| Linux | freedesktop Secret Service via libsecret (or compatible provider) | `LinuxCredentialVault` |

| Dimension | Finding |
|-----------|---------|
| License | First-party; no third-party license attribution for vault core |
| Fallback control | Full — reject file/DPAPI/in-memory production paths in composition |
| Typed mapping | Direct map to `CredentialVaultStatus` without fighting package defaults |
| Supply chain | Only OS native libraries already required by the host |
| Cost | Higher interop/test burden; mitigated by `CredentialVaultConformance` harness |
| Maintenance | Owned in-repo; aligned with Spec Kit contracts |

## Recommendation (awaits security + human approval)

```
RECOMMENDATION: FIRST-PARTY NATIVE ADAPTERS
PACKAGE STATUS: NO PACKAGE APPROVED
ktsu.CredentialCache 1.2.3: REJECTED
```

T009–T011 MUST implement the recommended first-party adapters (or an explicitly approved
replacement recorded in an amendment to this document). No NuGet vault package may be added to
`src/App/App.csproj` until this file’s status is changed to **Approved** with reviewer names.

## Conformance harness

Location:

- `tests/PlatformVault/ServiceBusExplorer.PlatformVaultTests.csproj`
- `tests/PlatformVault/CredentialVaultConformance.cs`

The harness proves, for any `ICredentialVault` under test:

1. Availability probe returns a typed status (never throws for unsupported/missing provider).
2. Store → retrieve → replace → delete round-trip preserves secret material only via
   `SensitiveCredential` (never via `ToString()` / JSON).
3. Missing retrieve returns `NotFound` without a credential payload.
4. Store/delete failure statuses are returned as typed results (not raw native exceptions).
5. **No credential fallback file** is created under a monitored working directory during the suite
   (guards against `credentials.dat`, DPAPI blobs, or plaintext secret files).

T009–T011 smoke tests MUST call this harness against the real OS adapter on the matching RID.

## Security review checklist (for approvers)

- [ ] Confirm rejection of `ktsu.CredentialCache` 1.2.3
- [ ] Confirm no production NuGet vault package is approved for T009–T011
- [ ] Confirm first-party adapter recommendation
- [ ] Confirm conformance harness covers no-file-fallback
- [ ] Approve proceeding to T009 / T010 / T011

**Approvals**: _(none yet — stop here for security + human review)_
