# RemoteLink Windows Service Mode

RemoteLink Desktop can run as a Windows service for unattended access, allowing remote desktop connections even when no user is logged in.

## Prerequisites

- Windows 10/11 or Windows Server 2016+
- Administrator privileges
- .NET 8.0 Runtime installed

## Installation

### Option 1: Using sc.exe (built-in Windows tool)

1. **Build the application:**
   ```powershell
   cd src\RemoteLink.Desktop\RemoteLink.Desktop
   dotnet publish -c Release -r win-x64 --self-contained false
   ```

2. **Install the service:**
   ```powershell
   sc.exe create RemoteLinkHost binPath="C:\Path\To\RemoteLink.Desktop.exe" start=auto
   ```

3. **Start the service:**
   ```powershell
   sc.exe start RemoteLinkHost
   ```

### Option 2: Using PowerShell New-Service cmdlet

1. **Build the application (if not already done):**
   ```powershell
   dotnet publish -c Release -r win-x64 --self-contained false
   ```

2. **Create and start the service:**
   ```powershell
   New-Service -Name "RemoteLinkHost" `
               -BinaryPathName "C:\Path\To\RemoteLink.Desktop.exe" `
               -DisplayName "RemoteLink Desktop Host" `
               -Description "Provides remote desktop access to this computer" `
               -StartupType Automatic
   
   Start-Service RemoteLinkHost
   ```

## Service Management

### Check service status
```powershell
Get-Service RemoteLinkHost
```

### Start service
```powershell
Start-Service RemoteLinkHost
```

### Stop service
```powershell
Stop-Service RemoteLinkHost
```

### Restart service
```powershell
Restart-Service RemoteLinkHost
```

### View service logs
Service logs are written to the Windows Event Log under Application logs. Filter by source "RemoteLinkHost".

```powershell
Get-EventLog -LogName Application -Source RemoteLinkHost -Newest 50
```

## Uninstallation

### Using sc.exe
```powershell
# Stop the service first
sc.exe stop RemoteLinkHost

# Delete the service
sc.exe delete RemoteLinkHost
```

### Using PowerShell
```powershell
Stop-Service RemoteLinkHost
Remove-Service RemoteLinkHost
```

## Console Mode vs Service Mode

The same executable can run in two modes:

- **Console Mode:** When run from a command prompt or PowerShell window. Shows console output and can be stopped with Ctrl+C.
- **Service Mode:** When run by the Windows Service Control Manager. Logs to the Event Log instead of console.

The application automatically detects which mode it's running in.

## Security Considerations

1. **Service Account:** By default, services run under the Local System account. For better security, consider running under a dedicated service account:
   ```powershell
   sc.exe config RemoteLinkHost obj="DOMAIN\ServiceAccount" password="password"
   ```

2. **Firewall:** Ensure TCP port 12346 is open in Windows Firewall:
   ```powershell
   New-NetFirewallRule -DisplayName "RemoteLink Host" `
                       -Direction Inbound `
                       -Protocol TCP `
                       -LocalPort 12346 `
                       -Action Allow
   ```

3. **PIN Security:** The service generates a new 6-digit PIN on startup. This PIN is logged to the Event Log and must be entered from the mobile client to establish a connection.

## Troubleshooting

### Service won't start
- Check Event Viewer → Windows Logs → Application for error messages
- Verify .NET 8.0 Runtime is installed
- Ensure the executable path in the service configuration is correct
- Check file permissions on the executable

### Can't connect from mobile client
- Verify the service is running: `Get-Service RemoteLinkHost`
- Check firewall rules allow inbound TCP on port 12346
- Ensure both devices are on the same local network
- Check Event Log for the current PIN

### Service crashes or stops unexpectedly
- Check Event Viewer for error details
- Verify network connectivity
- Ensure no other service is using port 12346

## Advanced Configuration

### Custom port
To use a different port, modify the DeviceInfo configuration in Program.cs:
```csharp
var localDevice = new DeviceInfo
{
    DeviceId = Environment.MachineName + "_" + Guid.NewGuid().ToString("N")[..8],
    DeviceName = Environment.MachineName,
    Type = DeviceType.Desktop,
    Port = 12346  // Change this to your desired port
};
```

### Recovery options
Configure service recovery options to automatically restart on failure:
```powershell
sc.exe failure RemoteLinkHost reset=86400 actions=restart/60000/restart/60000/restart/60000
```

## Limitations

- Service mode requires Windows (not available on Linux/macOS)
- Screen capture in service mode may have limitations depending on the logged-in user session
- Some interactive features (like clipboard sync) may not work when no user is logged in

## See Also

- [Windows Services Documentation](https://docs.microsoft.com/en-us/dotnet/core/extensions/windows-service)
- [sc.exe Command Reference](https://docs.microsoft.com/en-us/windows-server/administration/windows-commands/sc-create)
