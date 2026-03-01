using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RemoteLink.Desktop.Services;
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

        // Core shared services
        builder.Services.AddSingleton<ICommunicationService, TcpCommunicationService>();
        builder.Services.AddSingleton<IPairingService, PinPairingService>();
        builder.Services.AddSingleton<ISessionManager, SessionManager>();
        builder.Services.AddSingleton<IDeltaFrameEncoder, DeltaFrameEncoder>();
        builder.Services.AddSingleton<IPerformanceMonitor, PerformanceMonitor>();
        builder.Services.AddSingleton<IWakeOnLanService, WakeOnLanService>();
        builder.Services.AddSingleton<IMessagingService, MessagingService>();

        // Network discovery
        builder.Services.AddSingleton<INetworkDiscovery>(provider =>
        {
            var localDevice = new Shared.Models.DeviceInfo
            {
                DeviceId = Environment.MachineName + "_UI_" + Guid.NewGuid().ToString("N")[..8],
                DeviceName = Environment.MachineName,
                Type = Shared.Models.DeviceType.Desktop,
                Port = 12346
            };
            return new UdpNetworkDiscovery(localDevice);
        });

        // The background host service that manages the remote desktop server
        builder.Services.AddSingleton<RemoteDesktopHost>();

        // Remote desktop client (for outgoing connections to other hosts)
        // Uses its own TcpCommunicationService instances (separate from the host listener)
        builder.Services.AddSingleton<RemoteDesktopClient>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<RemoteDesktopClient>>();
            var discovery = provider.GetRequiredService<INetworkDiscovery>();
            return new RemoteDesktopClient(logger, discovery);
        });

        // Pages
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
