using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RemoteLink.Mobile.Services;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Mobile;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("RemoteLink Mobile Client Starting...");

        // Create host builder
        var builder = Host.CreateApplicationBuilder(args);
        
        // Configure services
        builder.Services.AddLogging();
        builder.Services.AddSingleton<INetworkDiscovery>(provider =>
        {
            var localDevice = new DeviceInfo
            {
                DeviceId = Environment.MachineName + "_Mobile_" + Guid.NewGuid().ToString("N")[..8],
                DeviceName = Environment.MachineName + " Mobile",
                Type = DeviceType.Mobile,
                Port = 12347
            };
            return new UdpNetworkDiscovery(localDevice);
        });
        
        builder.Services.AddHostedService<RemoteDesktopClient>();

        using var host = builder.Build();

        Console.WriteLine("Starting RemoteLink Mobile Client...");
        Console.WriteLine("Searching for desktop hosts on the network...");
        Console.WriteLine("Press Ctrl+C to stop the client.");

        try
        {
            await host.RunAsync();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Client stopped.");
        }
    }
}
