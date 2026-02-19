# AGENTS.md — RemoteLink AI Agent Instructions

> This file guides AI coding agents (GitHub Copilot Workspace, OpenAI Codex, Cursor, etc.)
> working on the RemoteLink repository. Read this file before reading any other file.

---

## Project Overview

**RemoteLink** is a free, open-source remote desktop application — a TeamViewer alternative for local and eventually internet-connected networks. It is built on **.NET 8** and **.NET MAUI**.

- **Full spec:** [`SPEC.md`](SPEC.md)
- **Feature tracker:** [`FEATURES.md`](FEATURES.md)
- **Repository:** https://github.com/DontDoThat21/RemoteLink
- **Branch:** `main`

---

## Solution Structure

```
RemoteLink/
├── src/
│   ├── RemoteLink.Shared/          # Shared library — interfaces, models, services
│   │   └── RemoteLink.Shared/
│   │       ├── Interfaces/         # All service contracts (IScreenCapture, ICommunicationService, etc.)
│   │       ├── Models/             # Data models (DeviceInfo, ScreenData, InputEvent, etc.)
│   │       ├── Services/           # Cross-platform service implementations
│   │       └── Security/           # TLS configuration
│   │
│   ├── RemoteLink.Desktop/         # Desktop host app (Windows, currently console/service)
│   │   └── RemoteLink.Desktop/
│   │       ├── Program.cs          # Entry point, DI registration
│   │       └── Services/           # Windows-specific implementations
│   │
│   └── RemoteLink.Mobile/          # .NET MAUI cross-platform client
│       └── RemoteLink.Mobile/
│           ├── App.cs              # MAUI Application entry
│           ├── MauiProgram.cs      # MAUI host builder + DI
│           ├── MainPage.cs         # Primary UI page (code-behind, no XAML)
│           └── Resources/          # MAUI icons, fonts, splash, images
│
└── tests/
    ├── RemoteLink.Desktop.Tests/   # xUnit tests for Desktop project
    └── RemoteLink.Shared.Tests/    # xUnit tests for Shared project
```

---

## Technology Stack

| Area | Technology |
|------|-----------|
| Language | C# 12, .NET 8 |
| Mobile/desktop UI | .NET MAUI (no XAML — all UI is code-behind using `Microsoft.Maui.Controls`) |
| Desktop host (current) | `Microsoft.Extensions.Hosting` — console / Windows Service |
| Desktop host (target) | .NET MAUI WinUI3 windowed application |
| Networking | TCP (length-prefixed JSON), UDP broadcast, TLS 1.2/1.3 (`SslStream`) |
| Screen capture | GDI32 / User32 P/Invoke (`BitBlt`, `GetDIBits`, `EnumDisplayMonitors`) |
| Input injection | User32 `SendInput` P/Invoke |
| Audio capture | WASAPI COM interop |
| Printing | `System.Drawing.Printing` |
| Session recording | FFmpeg process wrapper |
| DI / logging | `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging` |
| Testing | xUnit 2.4, `Microsoft.NET.Test.Sdk`, `coverlet.collector` |

---

## Build & Verification

**Always run the build and verify it passes before finishing any task.**

```powershell
# Restore dependencies
dotnet restore

# Build all projects (Desktop + Shared only — Mobile requires MAUI workload)
dotnet build src/RemoteLink.Desktop/RemoteLink.Desktop/RemoteLink.Desktop.csproj
dotnet build src/RemoteLink.Shared/RemoteLink.Shared/RemoteLink.Shared.csproj

# Run Desktop tests (these are the authoritative test suite — must stay green)
dotnet test tests/RemoteLink.Desktop.Tests/RemoteLink.Desktop.Tests/RemoteLink.Desktop.Tests.csproj

# Run Shared tests (note: DeltaFrameEncoderTests has pre-existing failures — see Known Issues)
dotnet test tests/RemoteLink.Shared.Tests/RemoteLink.Shared.Tests/RemoteLink.Shared.Tests.csproj
```

**Do NOT introduce new build errors.** The Mobile project may emit NETSDK1135 / NETSDK1202
warnings about iOS/macCatalyst target frameworks under .NET SDK 10 previews — these are
pre-existing and out of scope.

---

## Coding Standards & Conventions

### General
- **Nullable reference types enabled** — always annotate nullability correctly (`string?`, `T?`)
- **Implicit usings enabled** — do not add redundant `using System;` etc.
- **`AllowUnsafeBlocks`** is enabled in `RemoteLink.Desktop` for P/Invoke source generators
- Use `async`/`await` throughout; avoid `.Result` or `.Wait()`
- Use `CancellationToken` parameters on all long-running async methods
- XML doc comments (`/// <summary>`) on all `public` and `internal` types and members
- No `#region` blocks

### Namespaces
| Project | Root Namespace |
|---------|---------------|
| RemoteLink.Shared | `RemoteLink.Shared` |
| RemoteLink.Desktop | `RemoteLink.Desktop` |
| RemoteLink.Mobile | `RemoteLink.Mobile` |

### Interfaces (`RemoteLink.Shared/Interfaces/`)
- All service contracts live here as `I{Name}Service` or `I{Name}`
- Interfaces must be platform-agnostic (no P/Invoke, no Windows-only types)
- New interfaces follow the pattern of existing ones (XML doc on every member)

### Models (`RemoteLink.Shared/Models/`)
- Plain C# records or classes with `init` or mutable properties
- Nullable-enabled; required properties use `required` or constructor assignment
- No business logic in models — models are data bags only

### Services — Shared (`RemoteLink.Shared/Services/`)
- Cross-platform implementations of shared interfaces
- Must compile on all TFMs (net8.0, net8.0-android, net8.0-ios, net8.0-maccatalyst, net8.0-windows)
- Use `[SupportedOSPlatform]` / `OperatingSystem.IsWindows()` guards when needed

### Services — Desktop (`RemoteLink.Desktop/Services/`)
- Windows-specific implementations (`Windows*` prefix: `WindowsScreenCapture`, `WindowsInputHandler`, etc.)
- Each Windows implementation has a paired `Mock*` fallback for Linux/macOS dev environments
- Mock implementations live in the same `Services/` folder, prefixed with `Mock`
- P/Invoke declarations use `[DllImport]` or `[LibraryImport]` (source generator) — the latter requires `AllowUnsafeBlocks`

### UI — Mobile (`RemoteLink.Mobile/`)
- **All UI is code-behind C# — no XAML files**
- Build layouts programmatically using `Microsoft.Maui.Controls` types
- `MainThread.BeginInvokeOnMainThread()` for all UI updates from background threads
- Follow the existing `MainPage.cs` patterns for layout construction and event wiring

### DI Registration (`Program.cs` / `MauiProgram.cs`)
- Platform-conditional registration pattern:
  ```csharp
  if (OperatingSystem.IsWindows())
      builder.Services.AddSingleton<IFoo, WindowsFoo>();
  else
      builder.Services.AddSingleton<IFoo, MockFoo>();
  ```
- All new services must be registered before being used

### Testing
- Framework: **xUnit** with `Assert.*` (no FluentAssertions, no Moq — use hand-rolled fakes)
- Test doubles are named `Fake{Name}` (e.g., `FakeCommunicationService`)
- Mock service implementations for DI (not test-specific) are named `Mock{Name}`
- Test class naming: `{FeatureName}Tests` in the appropriate test project
- Tests that exercise Desktop-only services belong in `RemoteLink.Desktop.Tests`
- Tests that exercise Shared services belong in `RemoteLink.Shared.Tests`
- `InternalsVisibleTo("RemoteLink.Desktop.Tests")` is declared in `AssemblyInfo.cs` to allow white-box testing of internal members

---

## Architecture Rules

1. **`RemoteLink.Shared` has no dependency on `RemoteLink.Desktop` or `RemoteLink.Mobile`** — dependency only flows inward.
2. **`RemoteLink.Desktop` and `RemoteLink.Mobile` depend on `RemoteLink.Shared`** — never on each other.
3. **New protocols / message types** must be added to `ICommunicationService` (interface) + `TcpCommunicationService` (implementation) together, with a new `MsgType*` constant.
4. **New platform-specific capabilities** must have an interface in `Shared/Interfaces/`, a Windows implementation in `Desktop/Services/`, and a `Mock*` fallback.
5. **The `RemoteDesktopHost` `BackgroundService`** is the orchestration layer for the Desktop host — wire new services into it following the existing constructor injection and event-subscription pattern.

---

## Active Phases & Priorities

Work on phases **in order**. Do not skip ahead.

| Phase | Status | Priority | Summary |
|-------|--------|----------|---------|
| 1–4 | ✅ Complete | — | Core backend fully implemented |
| **5** | 📋 Next | 🔴 Critical | **Desktop GUI** — convert console host to MAUI WinUI3 windowed app (dashboard, system tray, settings, session toolbar) |
| **6** | 📋 Next | 🔴 High | **Mobile UI enhancements** — navigation shell, address book, file transfer UI, chat UI, QR scanner, virtual keyboard, settings |
| 7 | 📋 Planned | 🟡 Medium | Networking — NAT traversal, relay server, global IDs |
| 8 | 📋 Planned | 🟡 Medium | Security — accounts, 2FA, audit log |
| 9 | 📋 Planned | 🟢 Low | Collaboration — meetings, annotation, multi-session |

See [`FEATURES.md`](FEATURES.md) for the complete item-level tracker with status badges.

---

## Known Issues (Do Not Reintroduce)

| Issue | Location | Status |
|-------|----------|--------|
| `DeltaFrameEncoderTests` — tests reference `result.ImageData`, `result.DeltaRegions`, `result.FrameId`, `result.ReferenceFrameId`, `encoder.GetStats()` which no longer exist on the return type | `tests/RemoteLink.Shared.Tests/.../DeltaFrameEncoderTests.cs` | Pre-existing; do not work around by reverting `DeltaFrameEncoder` — fix the tests to match the current API |
| `MainPage.cs` — `DeviceInfo` is ambiguous between `Microsoft.Maui.Devices.DeviceInfo` and `RemoteLink.Shared.Models.DeviceInfo` | `src/RemoteLink.Mobile/.../MainPage.cs` | Pre-existing; fix by fully qualifying `RemoteLink.Shared.Models.DeviceInfo` at all ambiguous call sites |
| NETSDK1135 (iOS/macCatalyst) and NETSDK1202 (EOL workloads) | `RemoteLink.Mobile.csproj` | Pre-existing SDK/workload version mismatch under .NET SDK 10 preview; out of scope |
| `RemoteDesktopClient` passes `null!` for `ILogger` in `MainPage` constructor | `MainPage.cs` / `RemoteDesktopClient` | Runtime risk; fix when refactoring `MainPage` in Phase 6 |
| `MockClipboardService` unused event warning | `RemoteLink.Desktop/Services/MockClipboardService.cs` | Pre-existing warning; benign |

---

## When Adding a New Feature

1. **Read FEATURES.md** — find the matching item and check its status before starting
2. **Add the interface** to `RemoteLink.Shared/Interfaces/` if a new contract is needed
3. **Add the model(s)** to `RemoteLink.Shared/Models/` if new data structures are needed
4. **Add message type constant(s)** to `TcpCommunicationService` and extend `ICommunicationService` if network messages are involved
5. **Implement** the Windows version in `RemoteLink.Desktop/Services/` and the Mock fallback
6. **Register** the new service in `Program.cs` (Desktop) and/or `MauiProgram.cs` (Mobile) with platform-conditional guards
7. **Wire** into `RemoteDesktopHost` if the feature participates in the host session lifecycle
8. **Write tests** — minimum coverage: constructor null-guard, happy path, error/edge cases, event firing
9. **Update FEATURES.md** — change status from 📋 to ✅ with a session note at the bottom
10. **Verify build** — `dotnet build` must produce 0 errors before committing

---

## When Adding UI (MAUI)

- All UI is **code-behind C#** — do not create `.xaml` files
- New pages inherit from `ContentPage`
- Navigation uses `Shell` (preferred) or `NavigationPage` — follow the shell introduced in Phase 6
- All layout construction follows the builder pattern already in `MainPage.cs`:
  ```csharp
  var root = new StackLayout { Padding = ..., Spacing = ... };
  root.Children.Add(...);
  Content = new ScrollView { Content = root };
  ```
- Colours: primary blue `#1A73E8`, success green `#2E7D32`, danger red `#C62828`, surface `#FFFFFF`, muted `Colors.Gray`
- Bind to `INotifyPropertyChanged` properties on the page; use `new Binding(nameof(Prop), source: this)`
- Always marshal UI updates via `MainThread.BeginInvokeOnMainThread()`

---

## Commit Message Convention

```
feat(phase-N): short description of what was added

- Bullet summary of key changes
- Test count delta (e.g., "+25 tests, 310 total passing")
- Build status (e.g., "Build: 0 errors, N pre-existing warnings")
```

Example:
```
feat(phase-5): add Desktop MAUI WinUI3 dashboard window

- Converted RemoteLink.Desktop from console Exe to MAUI WinUI3 app
- Added MainWindow with ID/PIN display panel and partner connect form
- Added system tray icon with context menu (status, disconnect, quit)
- 12 new tests; 297 total passing
- Build: 0 errors, 3 pre-existing warnings
```

---

## Do Not

- ❌ Add NuGet packages without checking if an existing package already covers the need
- ❌ Use `Moq`, `NSubstitute`, or any mocking framework — use hand-rolled `Fake*` classes
- ❌ Add XAML files — all MAUI UI is code-behind
- ❌ Introduce `async void` (except MAUI event handlers where unavoidable)
- ❌ Use `.Result` or `.Wait()` on Tasks
- ❌ Revert existing implementations to fix test compilation errors — fix the tests instead
- ❌ Modify the `RemoteLink.Shared` public API without updating all consumers
- ❌ Skip writing tests for new services
- ❌ Leave FEATURES.md out of date after completing a feature
