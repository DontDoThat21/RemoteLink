## RemoteLink App Specification

### Overview

RemoteLink is a **free, open-source remote desktop application** built with **.NET 8 and .NET MAUI**. It provides a cross-platform alternative to TeamViewer, enabling users to remotely view and control Windows desktops from Android, iOS, macOS, and Windows devices — with peer-to-peer connectivity and no cloud dependencies.

> **Design inspiration:** TeamViewer's desktop and mobile applications serve as the primary UX reference for RemoteLink's UI, navigation, and feature set.

---

### Architecture

| Project | Technology | Purpose |
|---------|-----------|---------|
| `RemoteLink.Desktop` | .NET 8 — **must become .NET MAUI (WinUI3) windowed app** | Desktop host: screen capture, input handling, session management. Currently a headless console/Windows Service — **needs a full GUI**. |
| `RemoteLink.Mobile` | .NET MAUI (Android / iOS / macOS / Windows) | Mobile client: device discovery, remote viewer, touch input, connection management. Has basic single-page UI — **needs multi-page navigation shell and feature pages**. |
| `RemoteLink.Shared` | .NET 8 class library | Shared interfaces, models, services (networking, communication, pairing, encryption, codecs, file transfer, chat, audio, printing, etc.) |

```
RemoteLink Solution
├── src/
│   ├── RemoteLink.Shared/          # Common interfaces, models, and services
│   ├── RemoteLink.Desktop/         # Windows desktop host (→ MAUI WinUI3 GUI)
│   └── RemoteLink.Mobile/          # .NET MAUI cross-platform mobile/desktop client
├── tests/
│   ├── RemoteLink.Shared.Tests/    # Unit tests for shared components
│   └── RemoteLink.Desktop.Tests/   # Unit tests for desktop application
└── docs/                           # Documentation and guides
```

---

### Platform Targets

| Platform | Min Version | Role |
|----------|------------|------|
| **Windows 10/11** | 10.0.17763+ | Desktop host (GUI + service mode) and client |
| **Android** | 7.0+ (API 24) | Mobile client |
| **iOS** | 12.0+ | Mobile client |
| **macOS** | 10.15+ (Catalyst) | Desktop client |

---

### Core Features — Implemented ✅

1. **Network Discovery**
   - UDP broadcast/listen on port 12345 (5s interval, 15s timeout)
   - Automatic device detection on local network

2. **Screen Sharing**
   - Real-time GDI-based screen capture (BitBlt / GetDIBits) on Windows
   - Delta frame encoding (32×32 block change detection)
   - Adaptive quality (50–85 JPEG) based on connection metrics
   - Multi-monitor enumeration and selective capture

3. **Remote Input**
   - Mouse and keyboard via `SendInput` P/Invoke (Windows)
   - Touch-to-mouse translation (tap, double-tap, long-press, pan, scroll)
   - 15 keyboard shortcuts passthrough (Win+D, Alt+Tab, Ctrl+Shift+Esc, etc.)

4. **Authentication & Pairing**
   - 6-digit PIN with 5-minute TTL and 5-attempt lockout
   - Session token–based pairing flow

5. **Security**
   - TLS 1.2/1.3 with self-signed certificates
   - End-to-end encrypted TCP communication

6. **Session Management**
   - Full lifecycle (Pending → Connected → Disconnected / Ended)
   - Reconnect with attempt limits
   - Duration tracking

7. **Clipboard Sync**
   - Bidirectional text clipboard via Win32 P/Invoke
   - Polling with change detection (500ms)

8. **File Transfer**
   - 64KB chunked streaming with progress tracking
   - 2GB file size limit, MIME type detection

9. **Audio Streaming**
   - WASAPI loopback capture (48kHz / 16-bit / stereo)
   - 20ms PCM chunks, real-time transmission

10. **Session Recording**
    - FFmpeg wrapper, MP4 output (H.264 ultrafast)
    - Frame + audio recording, pause/resume

11. **Unattended Access**
    - Windows Service mode (`RemoteLinkHost`)
    - Auto-start, Event Log integration

12. **Wake-on-LAN**
    - UDP magic packet (102 bytes), MAC validation

13. **In-Session Chat**
    - Bidirectional text messaging, read receipts, unread tracking

14. **Remote Printing**
    - Image + text printing via `System.Drawing.Printing`
    - Job lifecycle (Queued → Printing → Completed/Failed/Cancelled)

15. **Connection Quality**
    - FPS / bandwidth / latency monitoring (2s updates)
    - Quality rating (Excellent / Good / Fair / Poor)

---

### Missing Features — UI & UX Gaps 🔴

> These are the critical gaps compared to TeamViewer. The backend services exist for many of these, but **no user-facing UI** has been built.

#### Desktop Host (currently headless console — no window at all)

| Gap | Description |
|-----|-------------|
| **No GUI** | The Desktop project is a console `Exe` / Windows Service. It must become a .NET MAUI WinUI3 windowed application with a proper dashboard. |
| No "Your ID & PIN" dashboard | TeamViewer prominently displays the device ID and session password on launch. |
| No partner connection panel | No way to initiate an outbound connection from the desktop host to another machine. |
| No system tray icon | No minimize-to-tray, no context menu for quick status/disconnect/quit. |
| No settings window | No UI to configure security, network, display, audio, recording, or startup preferences. |
| No session toolbar | No in-session overlay with actions (file transfer, chat, recording, quality, monitors, disconnect). |
| No desktop remote viewer | Host cannot act as a client to view/control another host. |
| No dark/light theme | No theming support. |

#### Mobile Client (basic single-page app)

| Gap | Description |
|-----|-------------|
| No navigation shell | Single `MainPage` — needs tabs or flyout (Connect, Devices, Files, Chat, Settings). |
| No manual ID + PIN entry | Only auto-discovery — no way to type in a remote ID. |
| No QR code scanner | Cannot scan the QR/PIN displayed on the desktop dashboard. |
| No address book | No persistent saved-devices list with friendly names. |
| No recent connections | No history of past sessions. |
| No file transfer UI | Backend exists (4.1) — no file picker, progress page, or receive flow. |
| No chat UI | Backend exists (4.6) — no chat bubble view or unread badge. |
| No session toolbar | No floating actions: keyboard, special keys, monitor selector, quality, disconnect. |
| No virtual keyboard overlay | No Ctrl/Alt/Shift/Win/Fn/arrow/Esc keys. |
| No multi-monitor picker | Backend exists (3.4) — no UI to switch between monitors. |
| No connection quality badge | Backend exists (3.5) — no on-screen indicator. |
| No settings page | No quality, gesture, audio, or notification preferences. |
| No theming | No dark/light mode. |

#### Networking & Connectivity

| Gap | Description |
|-----|-------------|
| Local network only | No NAT traversal, STUN/TURN, or relay servers. |
| No global device IDs | No internet-routable numeric ID system. |
| No signaling server | No coordination for devices on different LANs. |
| No proxy support | Cannot connect through corporate HTTP/SOCKS5 proxies. |

#### Security & Administration

| Gap | Description |
|-----|-------------|
| No user accounts | No registration/login for cross-device address book sync. |
| No 2FA (TOTP) | No two-factor authentication. |
| No trusted device allow-list | No whitelist for PIN-free connections. |
| No block/deny list | Cannot reject specific devices. |
| No per-session permissions | No view-only or restricted-feature mode. |
| No audit log | No timestamped connection history with actions. |
| No idle timeout | No auto-disconnect on inactivity. |

#### Collaboration & Productivity

| Gap | Description |
|-----|-------------|
| No meeting/presentation mode | No read-only broadcast to multiple viewers. |
| No screen annotation/whiteboard | No drawing tools on the shared screen. |
| No remote reboot + reconnect | No reboot-and-resume capability. |
| No remote system info panel | No OS/CPU/RAM/disk overview. |
| No remote command execution | No CLI/script runner from client. |
| No multi-session support | Cannot connect to multiple hosts simultaneously. |
| No drag-and-drop file transfer | No drag from local explorer into remote viewer. |
| No auto-update | No self-update mechanism. |

---

### Roadmap

See [FEATURES.md](FEATURES.md) for the full phased feature tracker. Summary:

- [x] **Phase 1** — Core foundation (architecture, models, interfaces, discovery, DI)
- [x] **Phase 2** — Basic functionality (communication, capture, input, pairing, sessions, mobile viewer)
- [x] **Phase 3** — Enhanced features (TLS, delta encoding, multi-monitor, clipboard, shortcuts)
- [x] **Phase 4** — Advanced features (file transfer, audio, recording, unattended, WoL, chat, printing)
- [ ] **Phase 5** — Desktop GUI application (convert console host → MAUI WinUI3 windowed app with dashboard, tray, settings, session toolbar)
- [ ] **Phase 6** — Mobile UI enhancements (navigation shell, address book, file transfer UI, chat UI, settings, QR scanner, virtual keyboard)
- [ ] **Phase 7** — Networking & connectivity (NAT traversal, relay server, global IDs, signaling, proxy)
- [ ] **Phase 8** — Security & administration (accounts, 2FA, trusted devices, audit log, permissions)
- [ ] **Phase 9** — Collaboration & productivity (meetings, annotation, remote reboot, multi-session, auto-update)

---

### Technical Requirements

#### Development Environment
- .NET 8.0 SDK or later
- .NET MAUI workload (`dotnet workload install maui`)
- Visual Studio 2022 17.8+ or Visual Studio 2026+
- Windows 10/11 (for desktop host development)
- Android SDK (API 24+), Xcode (iOS/macOS) for mobile targets

#### Runtime Requirements
- **Desktop Host:** Windows 10/11, .NET 8.0 Runtime
- **Mobile Client:** Android 7.0+ / iOS 12.0+ / macOS 10.15+ / Windows 10 1809+
- **Network:** Local network connectivity (internet connectivity planned for Phase 7)

---

### Build & Run

```bash
# Restore and build all projects
dotnet restore
dotnet build

# Install MAUI workload (if not already installed)
dotnet workload install maui

# Run desktop host (currently console mode)
dotnet run --project src/RemoteLink.Desktop/RemoteLink.Desktop/RemoteLink.Desktop.csproj

# Build mobile for specific platforms
dotnet build src/RemoteLink.Mobile/RemoteLink.Mobile/RemoteLink.Mobile.csproj -f net8.0-android
dotnet build src/RemoteLink.Mobile/RemoteLink.Mobile/RemoteLink.Mobile.csproj -f net8.0-windows10.0.19041.0

# Run tests
dotnet test
```

---

**Note:** This document is kept in sync with [FEATURES.md](FEATURES.md). Use `git commit` to track changes.