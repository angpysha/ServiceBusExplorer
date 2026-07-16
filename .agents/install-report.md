# Agent Pipeline Adaptation Report

## Detection

| Area | Detection | Source | Confidence |
| --- | --- | --- | --- |
| Runtime | C# and .NET 10, with legacy .NET Framework 4.7.2 projects | Project files | High |
| UI | Avalonia 11 with ReactiveUI; WinForms retained as a migration reference | PROJECT.md and project files | High |
| Architecture | App, Core, Services, and ViewModels separation | Solution and project references | High |
| Tests | xUnit with FluentAssertions | Test project | High |
| Cloud services | Azure messaging SDKs and Azure Identity | Package references | High |
| CI/CD | Existing GitHub Actions build, test, package, and release workflows | `.github/workflows/` | High |
| Tracker | beads | `.beads/` and repository guidance | High |
| Provisioning | Infrastructure-as-code is planned but not yet selected | PROJECT.md | Medium |
| Code search | codesearch index with 9,471 chunks across 459 files | Semantic search and statistics | High |

## Recommendation

- Runtime pack: `dotnet-minimal`
- Optional features: none detected
- Host: Cursor
- Tracker: beads
- Pull requests: manual GitHub workflow targeting `main`
- Conditional agents: `security-reviewer` for authentication and token handling; `tech-writer`
  for the full delivery lane
- Spec Kit: enabled with Cursor integration

Overall confidence is **high** because semantic search, project files, and `PROJECT.md` agree on the
runtime, architecture, security boundaries, and migration direction.

## Required human confirmation

Confirm the `dotnet-minimal` pack and listed agents before running `agentic-tool apply`.

## Post-apply

Spec Kit already exists in `.specify/`. After applying and synchronizing the catalog, verify the
installation and seed the project constitution from `PROJECT.md`.
