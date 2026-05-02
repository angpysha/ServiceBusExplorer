# ServiceBusExplorer: WinForms → Avalonia UI (.NET 8) Migration Plan

## Context

ServiceBusExplorer is a WinForms desktop application (net472) for managing Azure messaging services. The goal is to port it to Avalonia UI + ReactiveUI on .NET 8, targeting Windows, macOS, and Linux, while keeping the existing WinForms build functional during migration.

The repo has 9 projects. Four library projects (ServiceBus, EventHubs, Relay, EventGridExplorer, Utilities) already target netstandard2.0 and are essentially ready. The blockers are concentrated in **Common** (WCF + legacy SDK + App.config) and the **WinForms UI** (54 Forms/Controls, heavy GDI+, DataGridView).

---

## Dependency Map

```
ServiceBusExplorer (WinForms UI, net472)
└── Common (net472) ← MOST BLOCKERS
    ├── ServiceBus (netstandard2.0)  ✅ ready
    ├── EventHubs (netstandard2.0)   ✅ ready
    ├── Relay (netstandard2.0)       ✅ ready
    ├── EventGridExplorer (ns2.0)    ✅ ready
    ├── NotificationHubs (net472)    ⚠️ old SDK
    └── Utilities (netstandard2.0)   ✅ ready
```

---

## Blockers Requiring Decisions

### 🚧 Decision 1 — WCF Relay strategy

`Common/Helpers/ServiceBusHelper.cs` uses `System.ServiceModel` for WCF relay bindings (`NetOnewayRelayBinding`, `NetEventRelayBinding`, etc.) and `ServiceBusBindingHelper.cs` enumerates them by reflection. `App.config` has a `<system.serviceModel>` section with binding extensions.

**Options:**
- **A (Recommended):** Remove WCF relay support from Common. The `Relay.csproj` already uses `Microsoft.Azure.Relay 3.0.1` (non-WCF). Deprecate `ServiceBusBindingHelper.cs` and the legacy relay binding enumeration entirely.
- **B:** Add `CoreWCF.Http` + `CoreWCF.NetTcp` NuGet packages (CoreWCF is the .NET 8 community port of WCF server) — adds significant complexity for a rarely-used legacy path.

### 🚧 Decision 2 — Notification Hubs support

`Microsoft.Azure.NotificationHubs 1.0.9` targets net47x/netstandard1.3 and pulls in WCF. The project itself is `net472`.

**Options:**
- **A (Recommended):** Upgrade to `Microsoft.Azure.NotificationHubs` ≥ 4.x which supports .NET 8 cleanly (no WCF). API surface changed but the feature set is the same.
- **B:** Drop NH support and direct users to the Azure Portal.

### 🚧 Decision 3 — Legacy `WindowsAzure.ServiceBus` SDK

`Common.csproj` and `ServiceBusExplorer.csproj` reference `WindowsAzure.ServiceBus 7.0.1`. This SDK is deprecated and WCF-based; it cannot run on .NET 8. `ServiceBusHelper.cs` (the largest file in Common) is built around its `BrokeredMessage` / `NamespaceManager` / `MessagingFactory` APIs.

**Decision required:** Full migration to `Azure.Messaging.ServiceBus` + `Azure.Messaging.ServiceBus.Administration`. The ServiceBus.csproj already uses the modern SDK — the migration is moving `ServiceBusHelper.cs` to use it too. **This is the largest single migration task** and touches the core of all message send/receive/peek/DLQ operations.

### 🚧 Decision 4 — GDI+ custom controls replacement

Three controls use elaborate GDI+ rendering that has no direct Avalonia equivalent:
- `TrackBar.cs` (1,668 LOC, 37 Graphics calls, custom tick/gradient rendering)
- `Grouper.cs` (482 LOC, 50 Graphics calls, GraphicsPath shadows/rounded borders)
- `HeaderPanel.cs` (203 LOC, 11 Graphics calls)

**Options:**
- **A (Recommended):** Port to Avalonia's `DrawingContext` API (similar to WPF). Medium effort.
- **B:** Replace `TrackBar` with Avalonia's built-in `Slider`; replace `Grouper` with a styled `Border`/`GroupBox`. Less fidelity but less effort.

---

## UI Component Inventory

**54 total UI components** across 28 Forms + 26 Controls:

### 🟢 Simple (13) — basic dialogs, no custom rendering

`AboutForm`, `DateTimeForm`, `DateTimeRangeForm`, `TextForm`, `DeleteForm`, `ChangeQueueStatusForm`, `CreateEventGridTopicForm`, `ReceiveEventForm`, `PublishEventForm`, `EventGridConnectForm`, `ChangeStatusForm`, `TimeSpanControl`, `AccidentalDeletionPreventionCheckControl`

### 🟡 Medium (28) — heavy DataGridView, async state, thread marshaling

Key examples: `HandleQueueControl` (4,431 LOC, 324 DGV refs, 9 Invoke calls), `HandleSubscriptionControl` (3,000 LOC), `TestQueueControl`/`TestTopicControl` (2,187/2,190 LOC, 14 Invoke each), `ListenerControl`, `PartitionListenerControl`, `ConnectForm`, `ContainerForm`, `OptionForm`

### 🔴 Hard (13) — GDI+, WCF, P/Invoke, massive orchestration

`MainForm` (7,803 LOC), `HandleNotificationHubControl` (3,663 LOC, 458 DGV refs), `TrackBar` (custom GDI+), `Grouper` (custom GDI+), `HeaderPanel`, `Popup` (25+ P/Invoke calls), `DataGridViewColorPickerColumn`, `DataGridViewDateTimePickerColumn`, `NativeMethods`

---

## Migration Phases

### Phase 1 — Upgrade library projects to .NET 8
**Effort: S**

Projects with no blockers; only `TargetFramework` changes needed:
- `ServiceBus/ServiceBus.csproj`: `netstandard2.0` → `net8.0`
- `EventHubs/EventHubs.csproj`: `netstandard2.0` → `net8.0`
- `Relay/Relay.csproj`: `netstandard2.0` → `net8.0`
- `EventGridExplorerLibrary/EventGridExplorerLibrary.csproj`: `netstandard2.0` → `net8.0`
- `Utilities/Utilities.csproj`: `netstandard2.0` → `net8.0`

After this, add a new solution/project set (e.g., `src-avalonia/`) targeting net8.0. The WinForms solution remains intact and buildable throughout.

**Verify:** `dotnet build` for each project after TFM change. Run existing xUnit tests.

---

### Phase 2 — ViewModel extraction from existing Controls
**Effort: XL**

Before touching UI framework, extract business logic from WinForms code-behind into framework-agnostic ViewModels. This is the foundational step that makes the Avalonia port possible.

**Approach:**
1. Add a new `ServiceBusExplorer.ViewModels` project targeting `net8.0` with `ReactiveUI` and `DynamicData` packages.
2. For each `Handle*Control` and `Test*Control`, create a corresponding `*ViewModel` class:
   - Move all non-UI logic (Azure SDK calls, data transformation, state) into the ViewModel
   - Keep only rendering/layout/WinForms-specific code in the Control
   - Use `IObservable<T>` / `ReactiveCommand` for async operations
3. Priority order (start with least WinForms coupling):
   - `DashboardViewModel` (DashboardControl — 585 LOC)
   - `HandleRelayViewModel` (HandleRelayControl — 985 LOC)
   - `HandleEventHubViewModel` (HandleEventHubControl — 998 LOC)
   - `HandleTopicViewModel` / `HandleSubscriptionViewModel`
   - `HandleQueueViewModel` (4,431 LOC — largest)
   - `MainViewModel` (extracted from MainForm — orchestration)

**Key ViewModel patterns:**
- `ReactiveCommand<TIn, TOut>` for all async operations (send/receive/peek/DLQ)
- `ObservableCollection<T>` → `SourceList<T>` (DynamicData) for message lists
- Separate `IConnectionService` abstraction for ConnectForm logic

**Critical file:** `Common/Helpers/ServiceBusHelper.cs` is a 7,000+ LOC God-class. During this phase, split it into focused services: `ServiceBusQueueService`, `ServiceBusTopicService`, `ServiceBusAdminService`, etc., each taking `ServiceBusClient` / `ServiceBusAdministrationClient` injected as constructor parameters.

---

### Phase 3 — Avalonia shell + first screens
**Effort: M**

1. Create `src/ServiceBusExplorer.Avalonia/` project:
   ```xml
   <PackageReference Include="Avalonia" Version="11.*" />
   <PackageReference Include="Avalonia.Desktop" Version="11.*" />
   <PackageReference Include="Avalonia.ReactiveUI" Version="11.*" />
   ```
2. Implement `AppBootstrapper` with dependency injection (`Microsoft.Extensions.DependencyInjection`)
3. Build `MainWindow` shell: left tree-view pane (entity navigation) + right content area (dynamic control loading) — mirrors `MainForm`'s split panel layout
4. Port all **🟢 Simple tier** forms (13 components) as Avalonia `Window`/`UserControl` backed by ViewModels already extracted in Phase 2
5. Port `ConnectForm` → Avalonia `ConnectView` + existing `IConnectionService` ViewModel
6. Port `DashboardControl` → `DashboardView`

**WinForms build remains the primary shipping artifact** until Phase 4 completes.

---

### Phase 4 — Remaining Controls migration
**Effort: XL**

Migrate 🟡 Medium and 🔴 Hard tier components to Avalonia Views, using ViewModels from Phase 2.

**DataGridView → Avalonia DataGrid mapping:**
- Replace `DataGridView` + custom column types with Avalonia `DataGrid` + `DataGridTemplateColumn`
- `DataGridViewColorPickerColumn` → `DataGridTemplateColumn` with color picker `ContentControl`
- `DataGridViewDateTimePickerColumn` → `DataGridTemplateColumn` with `DatePicker`
- `DataGridViewDeleteButtonCell` → `DataGridTemplateColumn` with `Button`
- BindingSource patterns → `DynamicData` source lists bound to `DataGrid.ItemsSource`

**GDI+ custom controls → Avalonia equivalents:**
- `Grouper.cs` → Avalonia `GroupBox` or custom `ContentControl` with styled `Border` (rounded, shadow via `BoxShadow` property)
- `HeaderPanel.cs` → Avalonia `UserControl` with `DrawingContext` in `Render()` override
- `TrackBar.cs` → Avalonia `Slider` subclass with `Render()` override using `DrawingContext.DrawLine/DrawGeometry` (Decision 4A) or replace with styled `Slider` (Decision 4B)

**P/Invoke removal:**
- `NativeMethods.cs` P/Invoke calls (user32: `HideCaret`, `SendMessage`, `GetWindowLong`) are Windows-only and WinForms-specific. Replace with Avalonia equivalents:
  - Caret hiding → `TextBox` styling (Avalonia supports caret control via styles)
  - `SendMessage EM_SETCUEBANNER` → `TextBox.Watermark` property
  - `Popup.cs` window management → Avalonia's `Popup` control (built-in)

**Thread marshaling:**
- Replace all `Control.Invoke()` / `BeginInvoke()` patterns with `Dispatcher.UIThread.Post()` (Avalonia) or ReactiveUI's scheduler

**Migration order within this phase:**
1. `HandleRelayControl`, `HandleEventHubControl`, `HandleConsumerGroupControl` (≤1,000 LOC, moderate DGV)
2. `TestEventHubControl`, `TestRelayControl`, `TestSubscriptionControl`
3. `ListenerControl`, `PartitionListenerControl`
4. `TestQueueControl`, `TestTopicControl`
5. `HandleTopicControl`, `HandleSubscriptionControl`
6. `HandleQueueControl` (largest)
7. `HandleNotificationHubControl` (most DataGridView refs)
8. `MainForm` → `MainWindow` (final orchestration wiring)

---

### Phase 5 — Config system migration
**Effort: M**

Replace `System.Configuration` / App.config with `Microsoft.Extensions.Configuration`.

**Files affected:**
- `Common/Helpers/TwoFilesConfiguration.cs` — central configuration class, extensive `ConfigurationManager` use
- `ServiceBusExplorer/Forms/MainForm.cs`, `OptionForm.cs` — app settings read/write
- `App.config` — 5 custom `DictionarySectionHandler` sections + 22 appSettings keys

**Migration steps:**
1. Add `Microsoft.Extensions.Configuration.Json` + `Microsoft.Extensions.Configuration.Binder`
2. Create `appsettings.json` with the 22 app settings keys (preserve existing key names for compatibility)
3. Rewrite `TwoFilesConfiguration` to implement the same two-file merge logic using `IConfigurationBuilder.AddJsonFile(path, optional, reloadOnChange)`
4. Remove App.config `<system.serviceModel>` section entirely (WCF gone after Phase 6)
5. Replace `ConfigurationManager.AppSettings[]` reads with `IConfiguration["key"]`
6. Replace `ConfigurationManager.RefreshSection()` with `IConfigurationRoot.Reload()`
7. Add `IConfiguration` to the DI container in `AppBootstrapper`

**Key point:** The custom `serviceBusNamespaces` / `brokeredMessageInspectors` / `eventDataInspectors` sections used `DictionarySectionHandler`. In the new system, model these as JSON arrays/objects in `appsettings.json`.

---

### Phase 6 — WCF Relay handling
**Effort: M** (assuming Decision 1A — remove WCF relay)

**Files to refactor/remove:**
- `Common/Helpers/ServiceBusBindingHelper.cs` — enumerates WCF relay binding types via reflection. **Delete.** Replace the "binding type" concept with an enum mapping to modern relay connection options.
- `Common/Helpers/StringBodyWriter.cs` — extends `System.ServiceModel.Channels.BodyWriter`. Rewrite using `System.Xml.XmlWriter` directly.
- `Common/Helpers/LogBrokeredMessageInspector.cs` — implements `IBrokeredMessageInspector` (WCF message inspector). Port to an `Azure.Messaging.ServiceBus` plugin model (e.g., custom `ServiceBusPlugin` subclass).
- `Common/Helpers/LogEventDataInspector.cs` — same as above for Event Hubs.
- `Common/Helpers/RetryHelper.cs` — remove `CommunicationException` catch; replace with `ServiceBusException` from the modern SDK.
- `Common/Helpers/ServiceBusHelper.cs` — remove `BodyType.Wcf` and `CreateMessageForWcfReceiver()`. WCF-serialized messages will no longer be creatable from the tool (breaking change, must be documented in CHANGELOG).

**Also in this phase — complete legacy SDK migration in Common:**
- Remove `<PackageReference Include="WindowsAzure.ServiceBus" />` from `Common.csproj`
- Fully port remaining `ServiceBusHelper` methods from `BrokeredMessage` → `ServiceBusMessage` / `ServiceBusReceivedMessage`
- Port `NamespaceManager` calls → `ServiceBusAdministrationClient`

**If Decision 1B (CoreWCF):** Add `CoreWCF.Http`, `CoreWCF.NetTcp`, and `CoreWCF.Primitives`. Port binding configuration from App.config XML to programmatic C# setup. Effort becomes **L** and adds ~3k LOC of plumbing code.

---

## Critical Files

| File | Phase | Why Critical |
|------|-------|-------------|
| `src/Common/Helpers/ServiceBusHelper.cs` | 2, 6 | God-class; core of all SB operations; WCF + legacy SDK |
| `src/Common/Helpers/TwoFilesConfiguration.cs` | 5 | All app configuration; ConfigurationManager dependent |
| `src/Common/Helpers/ServiceBusBindingHelper.cs` | 6 | WCF relay binding enumeration; delete on Decision 1A |
| `src/ServiceBusExplorer/Forms/MainForm.cs` | 3, 4 | 7,803 LOC orchestrator; last to migrate |
| `src/ServiceBusExplorer/Controls/HandleQueueControl.cs` | 2, 4 | 4,431 LOC; 324 DGV refs; core queue UX |
| `src/ServiceBusExplorer/Controls/HandleNotificationHubControl.cs` | 2, 4 | 3,663 LOC; 458 DGV refs |
| `src/ServiceBusExplorer/Controls/TrackBar.cs` | 4 | Custom GDI+ rendering; needs Avalonia port |
| `src/ServiceBusExplorer/Controls/Grouper.cs` | 4 | Custom GDI+ rendering; needs Avalonia port |
| `src/ServiceBusExplorer/UIHelpers/NativeMethods.cs` | 4 | P/Invoke; Windows-only; replace with Avalonia APIs |
| `src/ServiceBusExplorer/App.config` | 5, 6 | WCF config sections + appSettings |

---

## Effort Summary

| Phase | Description | Effort |
|-------|-------------|--------|
| 1 | Library TFM upgrades | S |
| 2 | ViewModel extraction + ServiceBusHelper split | XL |
| 3 | Avalonia shell + Simple tier forms | M |
| 4 | Medium + Hard Controls migration | XL |
| 5 | Config system (App.config → appsettings.json) | M |
| 6 | WCF removal + legacy SDK migration | M (1A) / L (1B) |

Total: approx. 4–6 months of focused development for one engineer; 2–3 months with two engineers working Phases 2–4 in parallel.

---

## Verification Approach

After each phase:
1. **Phase 1:** `dotnet build` for each upgraded lib; run `dotnet test src/` confirming the WinForms build is unaffected
2. **Phase 2:** ViewModel unit tests (xUnit + FluentAssertions) with mocked `ServiceBusClient`; no UI needed
3. **Phase 3:** Launch Avalonia app on Windows, macOS, Linux; verify tree navigation + simple dialogs open correctly
4. **Phase 4:** Manual test against a live Azure Service Bus namespace using TESTING.md scenarios; verify send/receive/peek/DLQ on each entity type
5. **Phase 5:** Delete App.config; verify config loads from appsettings.json; verify connection strings persist between sessions
6. **Phase 6:** `dotnet build` with no `System.ServiceModel` references anywhere; verify Relay connection still works via `Microsoft.Azure.Relay`
