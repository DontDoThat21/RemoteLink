# Wake-on-LAN Guide

## Overview

Wake-on-LAN (WOL) allows you to remotely wake a sleeping or powered-off computer over the network by sending a special "magic packet" to its network interface card.

RemoteLink includes built-in Wake-on-LAN support to wake remote devices before connecting to them.

## Requirements

### Hardware Requirements

1. **Network Interface Card (NIC)** with Wake-on-LAN support
   - Most modern Ethernet adapters support WOL
   - Some Wi-Fi adapters support WOL, but it's less common and reliable
   - Check your NIC specifications or device manager

2. **BIOS/UEFI Configuration**
   - Wake-on-LAN must be enabled in BIOS/UEFI settings
   - Look for options like:
     - "Wake on LAN"
     - "Wake on PCI/PCIe"
     - "Power On By PCI-E/PCI"
     - "PME Event Wake Up"

3. **Power Connection**
   - Computer must be connected to power (not running on battery)
   - Some systems require ATX power supply with standby power

### Operating System Configuration

#### Windows

1. **Enable Wake-on-LAN in Network Adapter Settings:**
   - Open Device Manager
   - Expand "Network adapters"
   - Right-click your network adapter → Properties
   - Go to "Power Management" tab
   - Check: "Allow this device to wake the computer"
   - Check: "Only allow a magic packet to wake the computer"

2. **Advanced Settings (if available):**
   - Go to "Advanced" tab
   - Find "Wake on Magic Packet" or "Wake on Pattern Match"
   - Set to "Enabled"

3. **Disable Fast Startup (recommended):**
   - Control Panel → Power Options → Choose what the power buttons do
   - Click "Change settings that are currently unavailable"
   - Uncheck "Turn on fast startup"
   - Fast Startup can interfere with WOL

#### Linux

1. **Check if WOL is supported:**
   ```bash
   sudo ethtool eth0 | grep Wake-on
   ```
   Look for "Supports Wake-on: g" (g = magic packet)

2. **Enable Wake-on-LAN:**
   ```bash
   sudo ethtool -s eth0 wol g
   ```

3. **Make it persistent (Ubuntu/Debian):**
   - Edit `/etc/network/interfaces`:
     ```
     auto eth0
     iface eth0 inet dhcp
         post-up /sbin/ethtool -s eth0 wol g
     ```

   - Or create systemd service:
     ```bash
     sudo nano /etc/systemd/system/wol.service
     ```
     ```
     [Unit]
     Description=Enable Wake-on-LAN
     
     [Service]
     Type=oneshot
     ExecStart=/sbin/ethtool -s eth0 wol g
     
     [Install]
     WantedBy=multi-user.target
     ```
     ```bash
     sudo systemctl enable wol.service
     ```

#### macOS

1. **Enable Power Nap:**
   - System Preferences → Energy Saver
   - Check "Wake for network access"

2. **Note:** macOS WOL support varies by model and macOS version

## Usage in RemoteLink

### Finding a Device's MAC Address

#### Windows
```powershell
ipconfig /all
```
Look for "Physical Address" under your network adapter.

#### Linux
```bash
ip link show
# or
ifconfig
```
Look for "ether" or "HWaddr".

#### macOS
```bash
ifconfig
```
Look for "ether".

### Configuring MAC Address in RemoteLink

The MAC address can be added to discovered devices:

1. **Manual Configuration:**
   - Edit device information in RemoteLink Mobile app
   - Add MAC address in format: `XX:XX:XX:XX:XX:XX` or `XX-XX-XX-XX-XX-XX`

2. **Automatic Discovery (future enhancement):**
   - MAC addresses can be discovered via ARP lookup
   - Will be implemented in future versions

### Waking a Device

#### From Mobile App (UI Integration - planned)

1. Select offline device from device list
2. Tap "Wake" button
3. Wait 30-60 seconds for device to boot
4. Connect normally

#### Programmatic Usage

```csharp
// Wake by MAC address
var wolService = serviceProvider.GetRequiredService<IWakeOnLanService>();
bool sent = await wolService.SendWakePacketAsync("00:11:22:33:44:55");

// Wake a DeviceInfo object
var device = new DeviceInfo 
{
    DeviceName = "My PC",
    MacAddress = "AA:BB:CC:DD:EE:FF"
};
bool woken = await wolService.WakeDeviceAsync(device);

// Custom broadcast address (for specific subnet)
bool sent = await wolService.SendWakePacketAsync(
    "00:11:22:33:44:55", 
    broadcastAddress: "192.168.1.255"
);

// Custom port (default is 9, some use 7)
bool sent = await wolService.SendWakePacketAsync(
    "00:11:22:33:44:55", 
    port: 7
);
```

## Magic Packet Format

The Wake-on-LAN magic packet consists of:
- **6 bytes of 0xFF** (synchronization stream)
- **16 repetitions** of the target device's MAC address (6 bytes each)
- **Total:** 102 bytes

Example for MAC `00:11:22:33:44:55`:
```
FF FF FF FF FF FF  | Synchronization stream
00 11 22 33 44 55  | MAC address (1st repetition)
00 11 22 33 44 55  | MAC address (2nd repetition)
...                | (14 more repetitions)
00 11 22 33 44 55  | MAC address (16th repetition)
```

This packet is sent via UDP to the broadcast address on port 9 (or 7).

## Troubleshooting

### Device Doesn't Wake

1. **Verify BIOS/UEFI settings:**
   - Ensure WOL is enabled in BIOS
   - Check for "Deep Sleep" or "ErP Ready" settings (disable if present)

2. **Check network adapter settings:**
   - Windows: Ensure "Allow this device to wake the computer" is enabled
   - Linux: Run `sudo ethtool eth0 | grep Wake-on` and verify "g" is enabled

3. **Test WOL from another machine:**
   - Linux: `wakeonlan 00:11:22:33:44:55`
   - Windows: Use a WOL tool like "WakeMeOnLan"
   - This confirms if the issue is RemoteLink or the device itself

4. **Network configuration:**
   - Ensure device and client are on same subnet (or broadcast is configured correctly)
   - Check router/switch supports WOL packet forwarding
   - Some routers block broadcast UDP

5. **Power settings:**
   - Disable "Fast Startup" on Windows
   - Use "Sleep" or "Hibernate" instead of "Shutdown" for more reliable WOL

6. **Firewall:**
   - WOL uses UDP, ensure outbound UDP on port 9/7 is allowed
   - Some corporate firewalls block WOL packets

### Wrong MAC Address

- **Use wired connection MAC** (Ethernet), not Wi-Fi
- MAC addresses change if you replace network cards
- Virtual network adapters (VPN, Hyper-V, Docker) have different MACs

### Subnet/Broadcast Issues

If devices are on different subnets:
- Specify subnet-specific broadcast address: `192.168.1.255` instead of `255.255.255.255`
- Configure router to forward WOL packets (not all support this)
- Consider VPN or port forwarding for remote WOL (security risk)

## Network Ports

| Port | Protocol | Description |
|------|----------|-------------|
| 9    | UDP      | Standard WOL port (default) |
| 7    | UDP      | Alternative WOL port (Echo) |
| 12287 | UDP     | Another common alternative |

RemoteLink defaults to port 9, but can be configured to use any port.

## Security Considerations

1. **No Authentication:**
   - Wake-on-LAN has no authentication mechanism
   - Anyone with the MAC address can wake the device
   - Keep MAC addresses private

2. **Network Security:**
   - WOL only works on local network by default
   - Opening WOL to the internet is a security risk
   - Use VPN for remote WOL instead of port forwarding

3. **Physical Security:**
   - WOL bypasses OS-level security (device boots without password)
   - Ensure BIOS/disk encryption if physical security is a concern
   - RemoteLink still requires PIN authentication after wake

## Best Practices

1. **Test locally first:**
   - Wake device manually from another computer on same network
   - Verify BIOS and OS settings before troubleshooting RemoteLink

2. **Use Ethernet for WOL:**
   - Wi-Fi WOL is unreliable and not widely supported
   - Keep device connected via Ethernet cable

3. **Document MAC addresses:**
   - Store MAC addresses in RemoteLink device database
   - Keep backup list of MACs for important devices

4. **Hybrid sleep on Windows:**
   - Use "Sleep" mode instead of "Shutdown" for faster wake
   - Enables WOL while consuming minimal power

5. **Monitor wake attempts:**
   - Check RemoteLink logs for WOL packet send confirmations
   - Device may take 30-60 seconds to fully boot and respond

## References

- [Wake-on-LAN Wikipedia](https://en.wikipedia.org/wiki/Wake-on-LAN)
- [AMD Magic Packet Technology](https://www.amd.com/en/technologies/magic-packet)
- [Microsoft WOL Documentation](https://docs.microsoft.com/en-us/troubleshoot/windows-client/deployment/wake-on-lan-feature)
