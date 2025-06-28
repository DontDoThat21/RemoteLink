using Microsoft.Extensions.Logging;

namespace RemoteLink.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Configure logging
        builder.Services.AddLogging();
        builder.Logging.AddDebug();

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

        return builder.Build();
    }
}