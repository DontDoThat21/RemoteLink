# RemoteLink Desktop — Installation Guide

## Prerequisites

- **Windows 10** version 1809 (build 17763) or later
- **.NET 10 Runtime** — installed automatically when using the MSIX package; required manually for standalone builds

---

## Option A: MSIX Package (Recommended)

The MSIX package provides a full Windows install experience: Start Menu shortcut, automatic updates support, and clean uninstall.

### Install from a pre-built MSIX

1. **Trust the certificate** (first time only):
   - Right-click the `.msix` file → **Properties** → **Digital Signatures**
   - Select the signer → **Details** → **View Certificate** → **Install Certificate**
   - Choose **Local Machine** → **Place all certificates in the following store** → **Trusted People** → Finish
   - Or from PowerShell (admin):
     ```powershell
     Import-Certificate -FilePath scripts\RemoteLink-Dev.pfx `
         -CertStoreLocation Cert:\LocalMachine\TrustedPeople
     ```

2. **Install the package**:
   - Double-click the `.msix` file and click **Install**
   - Or from PowerShell:
     ```powershell
     Add-AppxPackage -Path publish\msix\RemoteLink.Desktop.UI_1.0.0.0_x64.msix
     ```

3. **Launch** from the Start Menu — search for **RemoteLink Desktop**.

### Build the MSIX from source

```powershell
# One-time setup: install the MAUI workload
dotnet workload install maui-windows

# Build the package (creates a self-signed cert if needed)
powershell -ExecutionPolicy Bypass -File scripts\Build-Msix.ps1

# Or build without creating a new certificate
powershell -ExecutionPolicy Bypass -File scripts\Build-Msix.ps1 -SkipCertificate
```

The output `.msix` file is placed in `publish\msix\`.

### Uninstall

- **Settings** → **Apps** → **Installed apps** → search **RemoteLink** → **Uninstall**
- Or from PowerShell:
  ```powershell
  Get-AppxPackage *RemoteLink* | Remove-AppxPackage
  ```

---

## Option B: Standalone (Unpackaged)

No MSIX, no certificate needed. Just a folder with the EXE and dependencies.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build-Msix.ps1 -Unpackaged
```

Output is placed in `publish\standalone\`. Run `RemoteLink.Desktop.UI.exe` directly.

> **Note:** Standalone mode does not create a Start Menu shortcut automatically.
> Auto-start is handled via registry (`HKCU\...\Run`) instead of the MSIX StartupTask API.

---

## Auto-Start with Windows

RemoteLink can launch automatically when you sign in to Windows.

1. Open RemoteLink → click the **gear icon** → go to the **Startup** section
2. Enable **Launch RemoteLink on Windows startup**
3. Optionally enable **Start minimised** to launch directly to the system tray
4. Optionally enable **Auto-start host service** to begin listening for connections immediately
5. Click **Save**

How it works:
- **MSIX mode**: uses the Windows `StartupTask` API (declared in `Package.appxmanifest`)
- **Standalone mode**: registers in `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` with a `--minimized` flag

---

## Start Menu & Taskbar

- **MSIX**: the installer automatically creates a Start Menu entry under **RemoteLink Desktop**. You can pin it to the taskbar from there.
- **Standalone**: create a shortcut to `RemoteLink.Desktop.UI.exe` manually, or pin it to the taskbar after launching.

---

## System Tray

RemoteLink minimises to the system tray when you close the window (instead of quitting). To fully exit, right-click the tray icon and select **Quit**.

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| "Publisher unknown" during MSIX install | Trust the self-signed certificate first (see step 1 above) |
| MSIX install fails with access denied | Run the `Import-Certificate` command from an admin PowerShell |
| App won't start on Windows login | Check **Settings → Apps → Startup** to ensure RemoteLink is not disabled by the OS |
| Build fails with "workload not installed" | Run `dotnet workload install maui-windows` |
| Firewall blocks connections | Allow TCP port **12346** and UDP port **12345** through Windows Firewall |

---

## Regenerating Icon Assets

The MSIX icon PNGs can be regenerated from the build scripts:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Generate-Icons.ps1
```

Or during the MSIX build:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build-Msix.ps1 -GenerateIcons
```
