// This file contains the MAUI program configuration that will be used
// when the .NET MAUI workload is installed and the project is fully converted

namespace RemoteLink.Mobile;

public static class MauiProgram
{
    /// <summary>
    /// Creates and configures the MAUI application
    /// Currently not functional until .NET MAUI workload is installed
    /// </summary>
    /// <returns>Configured MauiApp instance</returns>
    public static object CreateMauiApp()
    {
        // This method would be used when MAUI is fully available
        // For now, it's kept as a template for future implementation
        
        /* MAUI Implementation (requires workload):
        
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Configure logging
        builder.Services.AddLogging();

        // Configure network discovery service
        builder.Services.AddSingleton<RemoteLink.Shared.Interfaces.INetworkDiscovery>(provider =>
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
        builder.Services.AddSingleton<Services.RemoteDesktopClient>();

        // Configure pages
        builder.Services.AddSingleton<MainPage>();

        builder.Logging.AddDebug();

        return builder.Build();
        
        */

        throw new NotSupportedException(
            "MAUI functionality requires .NET MAUI workload. " +
            "Install with: dotnet workload install maui");
    }
}