# ServiceBusExplorer

> Living project brief. Created during `.agents adapt`.
> Edit anytime and run `.agents adapt --update` to refresh pipeline recommendations.

## Vision

ServiceBusExplorer is a cross-platform desktop application for developers and operators who
administer and troubleshoot Azure messaging services. It provides safe, efficient workflows for
browsing entities, inspecting message state, sending and receiving messages, managing dead-letter
queues, and importing or exporting configuration. Success means completing the migration from the
legacy Windows-only WinForms application to the modern Avalonia application without losing
supported functionality, while improving portability, maintainability, security, and testability.

## Stack

- Language and runtime: C# with .NET 10 for the modern application and domain libraries.
- UI: Avalonia 11 with ReactiveUI, targeting Windows, macOS, and Linux.
- Architecture: framework-independent Core, Services, and ViewModels projects consumed by the
  Avalonia App project.
- Legacy reference: .NET Framework 4.7.2 WinForms remains available only as a behavioral reference
  during migration and will be removed after all required functionality is migrated.
- Azure integrations: Azure Service Bus, Event Hubs, Event Grid, Relay, and Notification Hubs.
- Dependencies: NuGet with SDK-style projects for the modern application.
- CI/CD: GitHub Actions currently builds, tests, packages, and publishes the application.
- Provisioning: infrastructure-as-code and provisioning automation are planned; the specific Azure
  resources and IaC tool remain to be selected.

## Delivery

- Tracker: beads (`bd`).
- Pull requests: manual GitHub pull-request workflow targeting `main`.
- Development lane: full spec-driven development pipeline.
- Quality policy: warnings are treated as errors; applicable changes require automated tests.

## Open questions

- Which Azure resources, environments, and permissions must provisioning manage?
- Which infrastructure-as-code tool will be used for provisioning?
- What compatibility milestone permits removal of the WinForms projects?
