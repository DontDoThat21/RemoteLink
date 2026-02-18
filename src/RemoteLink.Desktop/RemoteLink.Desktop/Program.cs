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
        Console.WriteLine("RemoteLink Desktop Host Starting...");

        // Create host builder
        var builder = Host.CreateApplicationBuilder(args);
        
        // Configure services
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IScreenCapture, MockScreenCapture>();

        // Use platform-native input handler when running on Windows;
        // fall back to the mock handler on Linux/macOS (dev/test machines).
        if (OperatingSystem.IsWindows())
            builder.Services.AddSingleton<IInputHandler, WindowsInputHandler>();
        else
            builder.Services.AddSingleton<IInputHandler, MockInputHandler>();
        builder.Services.AddSingleton<ICommunicationService, TcpCommunicationService>();
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

        Console.WriteLine("Starting RemoteLink Desktop Host...");
        Console.WriteLine("Press Ctrl+C to stop the service.");

        try
        {
            await host.RunAsync();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Service stopped.");
        }
    }
}
