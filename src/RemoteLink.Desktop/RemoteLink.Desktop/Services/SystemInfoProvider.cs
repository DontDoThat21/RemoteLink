using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Collects host operating system, hardware, storage, and network details.
/// </summary>
public sealed class SystemInfoProvider : IRemoteSystemInfoProvider
{
    public Task<RemoteSystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (totalMemoryBytes, availableMemoryBytes) = GetMemoryInfo();
        var systemInfo = new RemoteSystemInfo
        {
            MachineName = Environment.MachineName,
            OperatingSystem = RuntimeInformation.OSDescription,
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            ProcessorName = GetProcessorName(),
            LogicalProcessorCount = Environment.ProcessorCount,
            TotalMemoryBytes = totalMemoryBytes,
            AvailableMemoryBytes = availableMemoryBytes,
            UptimeSeconds = Math.Max(0, Environment.TickCount64 / 1000)
        };

        foreach (var drive in DriveInfo.GetDrives())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!drive.IsReady)
                    continue;

                systemInfo.Disks.Add(new RemoteDiskInfo
                {
                    Name = drive.Name,
                    VolumeLabel = drive.VolumeLabel,
                    DriveFormat = drive.DriveFormat,
                    DriveType = drive.DriveType.ToString(),
                    TotalSizeBytes = drive.TotalSize,
                    AvailableFreeSpaceBytes = drive.AvailableFreeSpace
                });
            }
            catch
            {
            }
        }

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var ipProperties = networkInterface.GetIPProperties();
                var macAddress = networkInterface.GetPhysicalAddress();

                systemInfo.NetworkInterfaces.Add(new RemoteNetworkInterfaceInfo
                {
                    Name = networkInterface.Name,
                    Description = networkInterface.Description,
                    InterfaceType = networkInterface.NetworkInterfaceType.ToString(),
                    OperationalStatus = networkInterface.OperationalStatus.ToString(),
                    MacAddress = FormatMacAddress(macAddress),
                    IPv4Addresses = ipProperties.UnicastAddresses
                        .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(address => address.Address.ToString())
                        .ToList(),
                    IPv6Addresses = ipProperties.UnicastAddresses
                        .Where(address => address.Address.AddressFamily == AddressFamily.InterNetworkV6)
                        .Select(address => address.Address.ToString())
                        .ToList()
                });
            }
            catch
            {
            }
        }

        return Task.FromResult(systemInfo);
    }

    private static string GetProcessorName()
    {
        var processorName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        return string.IsNullOrWhiteSpace(processorName)
            ? RuntimeInformation.ProcessArchitecture.ToString()
            : processorName;
    }

    private static (long TotalMemoryBytes, long AvailableMemoryBytes) GetMemoryInfo()
    {
        if (OperatingSystem.IsWindows() && TryGetWindowsMemoryInfo(out var memoryStatus))
            return ((long)memoryStatus.ullTotalPhys, (long)memoryStatus.ullAvailPhys);

        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return (available, available);
    }

    private static string FormatMacAddress(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0 ? string.Empty : string.Join(':', bytes.Select(static b => b.ToString("X2")));
    }

    private static bool TryGetWindowsMemoryInfo(out MemoryStatusEx memoryStatus)
    {
        memoryStatus = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref memoryStatus);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}