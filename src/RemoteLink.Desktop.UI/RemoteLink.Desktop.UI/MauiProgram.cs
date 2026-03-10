using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RemoteLink.Desktop.Services;
using RemoteLink.Desktop.UI.Services;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;

namespace RemoteLink.Desktop.UI;

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
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Logging
        builder.Services.AddLogging();
        builder.Logging.AddDebug();

        // Platform-specific services (Windows implementations)
        builder.Services.AddSingleton<IScreenCapture, WindowsScreenCapture>();
        builder.Services.AddSingleton<IInputHandler, WindowsInputHandler>();
        builder.Services.AddSingleton<IClipboardService, WindowsClipboardService>();
        builder.Services.AddSingleton<IAudioCaptureService, WindowsAudioCaptureService>();
        builder.Services.AddSingleton<IPrintService, WindowsPrintService>();

        // Session recorder (mock by default; swap for SessionRecorder when FFmpeg is available)
        builder.Services.AddSingleton<ISessionRecorder, MockSessionRecorder>();

        var relayConfiguration = RelayConfiguration.FromEnvironment();
        var localDevice = DeviceIdentityManager.CreateOrLoadLocalDevice(
            "desktop-ui",
            Environment.MachineName,
            Shared.Models.DeviceType.Desktop,
            12346);
        relayConfiguration.ApplyTo(localDevice);

        builder.Services.AddSingleton(relayConfiguration);
        builder.Services.AddSingleton(localDevice);

        // File transfer (chunked streaming with progress tracking)
        builder.Services.AddSingleton<IFileTransferService, FileTransferService>();

        // Core shared services
        builder.Services.AddSingleton<ICommunicationService, AdaptiveCommunicationService>();
        builder.Services.AddSingleton<IConnectionRequestNotificationPublisher, LanConnectionRequestNotificationPublisher>();
        builder.Services.AddSingleton<INatTraversalService, NatTraversalService>();
        builder.Services.AddSingleton<IPairingService, PinPairingService>();
        builder.Services.AddSingleton<ISessionManager, SessionManager>();
        builder.Services.AddSingleton<IDeltaFrameEncoder, DeltaFrameEncoder>();
        builder.Services.AddSingleton<IPerformanceMonitor, PerformanceMonitor>();
        builder.Services.AddSingleton<IWakeOnLanService, WakeOnLanService>();
        builder.Services.AddSingleton<IMessagingService, MessagingService>();

        // Network discovery
        builder.Services.AddSingleton<INetworkDiscovery>(provider =>
        {
            return new UdpNetworkDiscovery(provider.GetRequiredService<Shared.Models.DeviceInfo>());
        });

        // The background host service that manages the remote desktop server
        builder.Services.AddSingleton<RemoteDesktopHost>();

        // Remote desktop client (for outgoing connections to other hosts)
        // Uses its own TcpCommunicationService instances (separate from the host listener)
        builder.Services.AddSingleton<RemoteDesktopClient>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<RemoteDesktopClient>>();
            var discovery = provider.GetRequiredService<INetworkDiscovery>();
            var natTraversal = provider.GetRequiredService<INatTraversalService>();
            var localDevice = provider.GetRequiredService<Shared.Models.DeviceInfo>();
            Func<ICommunicationService> commFactory = () => ActivatorUtilities.CreateInstance<AdaptiveCommunicationService>(provider);
            return new RemoteDesktopClient(logger, discovery, commFactory, natTraversal, localDevice);
        });

        // System tray service (minimize to tray, context menu)
        builder.Services.AddSingleton<WindowsSystemTrayService>();

        // Startup task service (auto-start with Windows, MSIX + registry)
        builder.Services.AddSingleton<StartupTaskService>();

        // Settings persistence
        builder.Services.AddSingleton<Shared.Interfaces.IAppSettingsService, Shared.Services.AppSettingsService>();

        // Pages
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddSingleton<Func<SettingsPage>>(provider =>
            () => provider.GetRequiredService<SettingsPage>());
        builder.Services.AddTransient<ChatPage>();
        builder.Services.AddSingleton<Func<ChatPage>>(provider =>
            () => provider.GetRequiredService<ChatPage>());
        builder.Services.AddTransient<RemoteViewerPage>();
        builder.Services.AddSingleton<Func<RemoteViewerPage>>(provider =>
            () => provider.GetRequiredService<RemoteViewerPage>());

        return builder.Build();
    }
}
