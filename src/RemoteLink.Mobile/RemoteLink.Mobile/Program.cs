using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RemoteLink.Mobile;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // Check if running in MAUI mode or console mode
        bool isMauiMode = args.Contains("--maui") || Environment.GetEnvironmentVariable("REMOTELINK_UI_MODE") == "MAUI";

        if (isMauiMode)
        {
            // MAUI Mode (when MAUI workload is available)
            Console.WriteLine("Starting RemoteLink Mobile in MAUI UI mode...");
            // MauiProgram.CreateMauiApp().Run();
            Console.WriteLine("MAUI mode requires .NET MAUI workload. Install with: dotnet workload install maui");
            Console.WriteLine("Falling back to console mode...");
        }

        // Console Mode with MAUI-ready architecture
        Console.WriteLine("RemoteLink Mobile Client Starting...");
        Console.WriteLine("Note: This version has MAUI-ready architecture but runs in console mode.");
        Console.WriteLine("To enable full MAUI UI, install the .NET MAUI workload and rebuild.");
        Console.WriteLine();

        // Create host builder with the same DI setup as MAUI would use
        var builder = Host.CreateApplicationBuilder(args);
        
        // Configure services (same as MauiProgram would)
        ConfigureServices(builder.Services);
        
        // Add the UI service (console-based for now, MAUI later)
        builder.Services.AddHostedService<ConsoleMobileUI>();

        using var host = builder.Build();

        Console.WriteLine("Starting RemoteLink Mobile Client...");
        Console.WriteLine("Searching for desktop hosts on the network...");
        Console.WriteLine("Press Ctrl+C to stop the client.");
        Console.WriteLine();

        try
        {
            await host.RunAsync();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Client stopped.");
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Configure logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
        });

        // Configure network discovery service
        services.AddSingleton<RemoteLink.Shared.Interfaces.INetworkDiscovery>(provider =>
        {
            var localDevice = new RemoteLink.Shared.Models.DeviceInfo
            {
                DeviceId = Environment.MachineName + "_Mobile_" + Guid.NewGuid().ToString("N")[..8],
                DeviceName = Environment.MachineName + " Mobile",
                Type = RemoteLink.Shared.Models.DeviceType.Mobile,
                Port = 12347
            };
            return new RemoteLink.Shared.Services.UdpNetworkDiscovery(localDevice);
        });

        // Configure the remote desktop client service
        services.AddSingleton<Services.RemoteDesktopClient>();
    }
}
