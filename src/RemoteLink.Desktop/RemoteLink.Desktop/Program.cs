using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RemoteLink.Desktop.Services;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Desktop;

class Program
{
    static async Task Main(string[] args)
    {
        // Create host builder
        var builder = Host.CreateApplicationBuilder(args);

        // Enable Windows service support
        // When running as a Windows service, this allows the app to respond to
        // service control manager commands (start/stop/pause).
        // In console mode, this has no effect.
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "RemoteLinkHost";
        });

        // Configure services
        builder.Services.AddLogging();
        // Use real GDI screen capture on Windows; fall back to mock on Linux/macOS.
        if (OperatingSystem.IsWindows())
            builder.Services.AddSingleton<IScreenCapture, WindowsScreenCapture>();
        else
            builder.Services.AddSingleton<IScreenCapture, MockScreenCapture>();

        // Use platform-native input handler when running on Windows;
        // fall back to the mock handler on Linux/macOS (dev/test machines).
        if (OperatingSystem.IsWindows())
            builder.Services.AddSingleton<IInputHandler, WindowsInputHandler>();
        else
            builder.Services.AddSingleton<IInputHandler, MockInputHandler>();

        // Use Windows clipboard service on Windows; fall back to mock on Linux/macOS.
        if (OperatingSystem.IsWindows())
            builder.Services.AddSingleton<IClipboardService, WindowsClipboardService>();
        else
            builder.Services.AddSingleton<IClipboardService, MockClipboardService>();

        // Use Windows audio capture on Windows; fall back to mock on Linux/macOS.
        if (OperatingSystem.IsWindows())
            builder.Services.AddSingleton<IAudioCaptureService, WindowsAudioCaptureService>();
        else
            builder.Services.AddSingleton<IAudioCaptureService, MockAudioCaptureService>();

        // Session recorder (requires FFmpeg for real recording)
        // TODO: Add configuration to choose SessionRecorder vs MockSessionRecorder
        builder.Services.AddSingleton<ISessionRecorder, MockSessionRecorder>();

        builder.Services.AddSingleton<ICommunicationService, TcpCommunicationService>();
        builder.Services.AddSingleton<IPairingService, PinPairingService>();
        builder.Services.AddSingleton<ISessionManager, SessionManager>();
        builder.Services.AddSingleton<IDeltaFrameEncoder, DeltaFrameEncoder>();
        builder.Services.AddSingleton<IPerformanceMonitor, PerformanceMonitor>();
        builder.Services.AddSingleton<IWakeOnLanService, WakeOnLanService>();
        builder.Services.AddSingleton<INetworkDiscovery>(provider =>
        {
            var localDevice = new DeviceInfo
            {
                DeviceId = Environment.MachineName + "_" + Guid.NewGuid().ToString("N")[..8],
                DeviceName = Environment.MachineName,
                Type = DeviceType.Desktop,
                Port = 12346
            };
            return new UdpNetworkDiscovery(localDevice);
        });
        
        builder.Services.AddHostedService<RemoteDesktopHost>();

        using var host = builder.Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        // Detect if running as Windows service
        bool isWindowsService = OperatingSystem.IsWindows() && 
                                 !Environment.UserInteractive;

        if (isWindowsService)
        {
            logger.LogInformation("RemoteLink Desktop Host starting as Windows service");
        }
        else
        {
            Console.WriteLine("RemoteLink Desktop Host Starting...");
            Console.WriteLine("Running in console mode. Press Ctrl+C to stop.");
        }

        try
        {
            await host.RunAsync();
        }
        catch (OperationCanceledException)
        {
            if (!isWindowsService)
            {
                Console.WriteLine("Service stopped.");
            }
        }
    }
}
