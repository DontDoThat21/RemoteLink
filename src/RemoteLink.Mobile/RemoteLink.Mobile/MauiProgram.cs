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

        var relayConfiguration = RelayConfiguration.FromEnvironment();
        var signalingConfiguration = SignalingConfiguration.FromEnvironment();
        var proxyConfiguration = ProxyConfiguration.FromEnvironment();
        var secureTunnelConfiguration = SecureTunnelConfiguration.FromEnvironment();
        var localDevice = DeviceIdentityManager.CreateOrLoadLocalDevice(
            "mobile-client",
            Environment.MachineName + " Mobile",
            DeviceType.Mobile,
            12347);
        relayConfiguration.ApplyTo(localDevice);
        secureTunnelConfiguration.ApplyTo(localDevice);

        builder.Services.AddSingleton(relayConfiguration);
        builder.Services.AddSingleton(signalingConfiguration);
        builder.Services.AddSingleton(proxyConfiguration);
        builder.Services.AddSingleton(secureTunnelConfiguration);
        builder.Services.AddSingleton(localDevice);
        builder.Services.AddSingleton<ISignalingService, SignalingService>();

        // Network discovery service
        builder.Services.AddSingleton<INetworkDiscovery>(provider =>
        {
            return new UdpNetworkDiscovery(provider.GetRequiredService<DeviceInfo>());
        });

        // Saved devices address book
        builder.Services.AddSingleton<ISavedDevicesService, SavedDevicesService>();

        // Connection history
        builder.Services.AddSingleton<IConnectionHistoryService, ConnectionHistoryService>();
        builder.Services.AddSingleton<IUserAccountService, UserAccountService>();

        // Shared application settings
        builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();
        builder.Services.AddSingleton<IAppUpdateService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<AppUpdateService>>();
            var platform = Microsoft.Maui.Devices.DeviceInfo.Current.Platform;
            var options = new AppUpdateOptions
            {
                ProductName = "RemoteLink Mobile",
                CurrentVersion = AppInfo.Current.VersionString,
                Platform = platform == Microsoft.Maui.Devices.DevicePlatform.Android
                    ? AppUpdatePlatform.MobileAndroid
                    : platform == Microsoft.Maui.Devices.DevicePlatform.iOS
                        ? AppUpdatePlatform.MobileIos
                        : platform == Microsoft.Maui.Devices.DevicePlatform.MacCatalyst
                            ? AppUpdatePlatform.MobileMacCatalyst
                            : AppUpdatePlatform.MobileWindows,
                WindowsStoreUrl = Environment.GetEnvironmentVariable("REMOTELINK_MOBILE_STORE_URL_WINDOWS"),
                AndroidStoreUrl = Environment.GetEnvironmentVariable("REMOTELINK_MOBILE_STORE_URL_ANDROID"),
                IosStoreUrl = Environment.GetEnvironmentVariable("REMOTELINK_MOBILE_STORE_URL_IOS"),
                MacCatalystStoreUrl = Environment.GetEnvironmentVariable("REMOTELINK_MOBILE_STORE_URL_MACCATALYST")
            };

            return new AppUpdateService(new HttpClient(), logger, options);
        });
        builder.Services.AddSingleton<IAppLockService, AppLockService>();
        builder.Services.AddSingleton<IDevicePhotoLibraryService, DevicePhotoLibraryService>();
        builder.Services.AddSingleton<IncomingConnectionNotificationListener>();
        builder.Services.AddSingleton<INatTraversalService, NatTraversalService>();

        // Remote desktop client (singleton shared across all pages)
        builder.Services.AddSingleton<RemoteDesktopClient>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<RemoteDesktopClient>>();
            var discovery = provider.GetRequiredService<INetworkDiscovery>();
            var natTraversal = provider.GetRequiredService<INatTraversalService>();
            var localDevice = provider.GetRequiredService<DeviceInfo>();
            Func<ICommunicationService> commFactory = () => ActivatorUtilities.CreateInstance<AdaptiveCommunicationService>(provider);
            return new RemoteDesktopClient(logger, discovery, commFactory, natTraversal, localDevice);
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
