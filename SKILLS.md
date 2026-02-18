# SKILLS.md — RemoteLink Development Guide

## Project Overview

RemoteLink is a free, open-source remote desktop app (TeamViewer alternative) for local network use.

- **Tech:** .NET 8, C#, .NET MAUI (mobile), Console app (desktop host)
- **Architecture:** 3-project solution — Shared, Desktop, Mobile
- **Repo:** https://github.com/DontDoThat21/RemoteLink
- **License:** MIT

## Solution Structure

```
RemoteLink/
├── RemoteLink.sln
├── src/
│   ├── RemoteLink.Shared/          # Interfaces, models, shared services
│   │   ├── Interfaces/             # IScreenCapture, IInputHandler, ICommunicationService, INetworkDiscovery
│   │   ├── Models/                 # DeviceInfo, InputEvent, ScreenData, RemoteSession
│   │   └── Services/              # UdpNetworkDiscovery
│   ├── RemoteLink.Desktop/        # Windows host (console/BackgroundService)
│   │   ├── Program.cs             # Entry point, DI setup
│   │   └── Services/              # RemoteDesktopHost, MockScreenCapture, MockInputHandler
│   └── RemoteLink.Mobile/         # .NET MAUI client
│       ├── App.cs                 # Application entry
│       ├── MainPage.cs            # Main UI (discovery + connection)
│       ├── MauiProgram.cs         # DI + MAUI config
│       └── Services/              # RemoteDesktopClient
├── tests/
│   ├── RemoteLink.Shared.Tests/   # Model + service tests
│   └── RemoteLink.Desktop.Tests/  # Desktop service tests
└── docs/
```

## Network Configuration

| Service | Protocol | Port |
|---------|----------|------|
| Discovery | UDP broadcast | 12345 |
| Desktop Host | TCP | 12346 |
| Mobile Client | TCP | 12347 |

- Broadcast interval: 5 seconds
- Device timeout: 15 seconds
- Default capture: 10 FPS, 75% JPEG quality

## Development Workflow

1. **Feature tracking:** Update `FEATURES.md` when completing any feature
2. **Testing:** All features must have xUnit tests before marking ✅
3. **Branching:** Work on `main` (solo project)
4. **Build target:** This is a .NET 8 project — build with `dotnet build`
5. **Test:** `dotnet test` from solution root
6. **No MAUI build on Linux:** Mobile project won't build on this Ubuntu server. Desktop + Shared will.

## Build Notes (Ubuntu Server)

- ❌ Cannot build `RemoteLink.Mobile` — requires MAUI workload + platform SDKs
- ✅ Can build `RemoteLink.Shared` and `RemoteLink.Desktop` (net8.0)
- ✅ Can run all Shared + Desktop tests
- For Mobile changes: write code, push, build on a Windows/Mac machine

## Current Priority

Fix build-breaking issues first, then implement Phase 2 (real communication, screen capture, input handling).

## Code Style

- Nullable reference types enabled
- Implicit usings enabled
- Interface-driven design with DI
- Async/await throughout
- XML doc comments on public APIs

## Key Interfaces to Implement

### ICommunicationService (highest priority)
Real TCP or SignalR implementation for host↔client data exchange. This unlocks screen streaming and input relay.

### IScreenCapture (Windows)
Replace MockScreenCapture with real Windows screen capture using:
- Windows.Graphics.Capture API (preferred, modern)
- Desktop Duplication API (DirectX, high performance)
- BitBlt/GDI+ (fallback, simpler)

### IInputHandler (Windows)
Replace MockInputHandler with real Windows input simulation using:
- SendInput API (user32.dll P/Invoke)
- Command execution with allowlist security
