using Microsoft.Extensions.Logging;
using RemoteLink.Mobile.Services;
using RemoteLink.Shared.Interfaces;
using ZXing.Net.Maui.Controls;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using DeviceInfo = RemoteLink.Shared.Models.DeviceInfo;
using DeviceType = RemoteLink.Shared.Models.DeviceType;

namespace RemoteLink.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseBarcodeReader()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Logging
        builder.Services.AddLogging();
        builder.Logging.AddDebug();

        // Network discovery service
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

        // Saved devices address book
        builder.Services.AddSingleton<ISavedDevicesService, SavedDevicesService>();

        // Connection history
        builder.Services.AddSingleton<IConnectionHistoryService, ConnectionHistoryService>();

        // Shared application settings
        builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();
        builder.Services.AddSingleton<IAppLockService, AppLockService>();
        builder.Services.AddSingleton<IncomingConnectionNotificationListener>();

        // Remote desktop client (singleton shared across all pages)
        builder.Services.AddSingleton<RemoteDesktopClient>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<RemoteDesktopClient>>();
            var discovery = provider.GetRequiredService<INetworkDiscovery>();
            return new RemoteDesktopClient(logger, discovery);
        });

        builder.Services.AddSingleton<MobileChatSession>();

        // Shell
        builder.Services.AddSingleton<AppShell>();

        // Pages — all tabs
        builder.Services.AddTransient<ConnectPage>();
        builder.Services.AddTransient<DevicesPage>();
        builder.Services.AddTransient<FilesPage>();
        builder.Services.AddTransient<MobileChatPage>();
        builder.Services.AddTransient<MobileSettingsPage>();
        builder.Services.AddTransient<RecentConnectionsPage>();
        builder.Services.AddTransient<AppLockPage>();

        return builder.Build();
    }
}
