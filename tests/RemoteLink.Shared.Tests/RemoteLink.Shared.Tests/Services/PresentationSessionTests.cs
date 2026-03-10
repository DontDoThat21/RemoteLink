using System.Net;
using System.Net.Sockets;
using RemoteLink.Shared.Models;
using RemoteLink.Shared.Security;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests.Services;

public class PresentationSessionTests
{
    [Fact]
    public async Task PresentationSessionHost_BroadcastsScreenFramesToMultipleViewers()
    {
        int port = GetFreePort();
        var tlsConfiguration = new TlsConfiguration { Enabled = false };

        await using var host = new PresentationSessionHost(tlsConfiguration);
        await host.StartAsync(port);
        await host.ActivateAsync("123456", "All Hands");

        var screen1Tcs = new TaskCompletionSource<ScreenData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var screen2Tcs = new TaskCompletionSource<ScreenData>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var viewer1 = new PresentationSessionClient(tlsConfiguration);
        viewer1.ScreenDataReceived += (_, screen) => screen1Tcs.TrySetResult(screen);

        await using var viewer2 = new PresentationSessionClient(tlsConfiguration);
        viewer2.ScreenDataReceived += (_, screen) => screen2Tcs.TrySetResult(screen);

        var presentationHost = new DeviceInfo
        {
            DeviceId = "presentation-host",
            DeviceName = "Presentation Host",
            IPAddress = "127.0.0.1",
            PresentationPort = port,
            SupportsPresentationMode = true,
            PresentationSessionActive = true,
            Type = DeviceType.Desktop
        };

        var response1 = await viewer1.ConnectAsync(presentationHost, "123456", "viewer-1", "Viewer 1");
        var response2 = await viewer2.ConnectAsync(presentationHost, "123456", "viewer-2", "Viewer 2");

        Assert.True(response1.Success);
        Assert.True(response2.Success);
        Assert.NotNull(response1.SessionPermissions);
        Assert.False(response1.SessionPermissions!.AllowRemoteInput);

        await WaitForViewerCountAsync(host, 2);

        var screenData = new ScreenData
        {
            FrameId = "frame-1",
            Width = 1920,
            Height = 1080,
            Format = ScreenDataFormat.JPEG,
            Quality = 70,
            ImageData = [1, 2, 3, 4, 5]
        };

        var broadcastResult = await host.BroadcastScreenDataAsync(screenData);
        var received1 = await screen1Tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var received2 = await screen2Tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(2, broadcastResult.SuccessfulViewerCount);
        Assert.Equal(screenData.ImageData.LongLength * 2, broadcastResult.TotalBytesSent);
        Assert.Equal("frame-1", received1.FrameId);
        Assert.Equal("frame-1", received2.FrameId);
    }

    [Fact]
    public async Task PresentationSessionClient_RejectsInvalidPin()
    {
        int port = GetFreePort();
        var tlsConfiguration = new TlsConfiguration { Enabled = false };

        await using var host = new PresentationSessionHost(tlsConfiguration);
        await host.StartAsync(port);
        await host.ActivateAsync("123456", "Town Hall");

        await using var viewer = new PresentationSessionClient(tlsConfiguration);
        var response = await viewer.ConnectAsync(
            new DeviceInfo
            {
                DeviceId = "presentation-host",
                DeviceName = "Presentation Host",
                IPAddress = "127.0.0.1",
                PresentationPort = port,
                SupportsPresentationMode = true,
                PresentationSessionActive = true,
                Type = DeviceType.Desktop
            },
            "999999",
            "viewer-1",
            "Viewer 1");

        Assert.False(response.Success);
        Assert.Equal("Invalid presentation PIN.", response.Message);
        Assert.Equal(0, host.ViewerCount);
    }

    [Fact]
    public async Task PresentationSessionHost_BroadcastsAnnotationsToConnectedViewers()
    {
        int port = GetFreePort();
        var tlsConfiguration = new TlsConfiguration { Enabled = false };

        await using var host = new PresentationSessionHost(tlsConfiguration);
        await host.StartAsync(port);
        await host.ActivateAsync("123456", "Design Review");

        var annotation1Tcs = new TaskCompletionSource<PresentationAnnotationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var annotation2Tcs = new TaskCompletionSource<PresentationAnnotationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var viewer1 = new PresentationSessionClient(tlsConfiguration);
        viewer1.AnnotationReceived += (_, annotation) => annotation1Tcs.TrySetResult(annotation);

        await using var viewer2 = new PresentationSessionClient(tlsConfiguration);
        viewer2.AnnotationReceived += (_, annotation) => annotation2Tcs.TrySetResult(annotation);

        var presentationHost = new DeviceInfo
        {
            DeviceId = "presentation-host",
            DeviceName = "Presentation Host",
            IPAddress = "127.0.0.1",
            PresentationPort = port,
            SupportsPresentationMode = true,
            PresentationSessionActive = true,
            Type = DeviceType.Desktop
        };

        Assert.True((await viewer1.ConnectAsync(presentationHost, "123456", "viewer-1", "Viewer 1")).Success);
        Assert.True((await viewer2.ConnectAsync(presentationHost, "123456", "viewer-2", "Viewer 2")).Success);
        await WaitForViewerCountAsync(host, 2);

        var annotationMessage = new PresentationAnnotationMessage
        {
            Action = PresentationAnnotationAction.Upsert,
            ChangedByDeviceId = "host-presenter",
            ChangedAtUtc = DateTime.UtcNow,
            Annotation = new PresentationAnnotation
            {
                AnnotationId = "annotation-1",
                Kind = PresentationAnnotationKind.Arrow,
                CreatedByDeviceId = "host-presenter",
                CreatedAtUtc = DateTime.UtcNow,
                Style = new PresentationAnnotationStyle
                {
                    StrokeColor = "#7C3AED",
                    StrokeWidth = 5
                },
                Points =
                [
                    new PresentationAnnotationPoint { X = 0.1, Y = 0.2 },
                    new PresentationAnnotationPoint { X = 0.8, Y = 0.9 }
                ]
            }
        };

        var result = await host.BroadcastAnnotationAsync(annotationMessage);
        var received1 = await annotation1Tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var received2 = await annotation2Tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(2, result.SuccessfulViewerCount);
        Assert.Equal(PresentationAnnotationAction.Upsert, received1.Action);
        Assert.Equal(PresentationAnnotationKind.Arrow, received1.Annotation?.Kind);
        Assert.Equal("annotation-1", received2.Annotation?.AnnotationId);
    }

    [Fact]
    public async Task PresentationSessionClient_SendsAnnotationsThatAreBroadcastToAllViewersAndStoredOnHost()
    {
        int port = GetFreePort();
        var tlsConfiguration = new TlsConfiguration { Enabled = false };

        await using var host = new PresentationSessionHost(tlsConfiguration);
        await host.StartAsync(port);
        await host.ActivateAsync("123456", "Team Sync");

        var annotation1Tcs = new TaskCompletionSource<PresentationAnnotationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var annotation2Tcs = new TaskCompletionSource<PresentationAnnotationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var viewer1 = new PresentationSessionClient(tlsConfiguration);
        viewer1.AnnotationReceived += (_, annotation) => annotation1Tcs.TrySetResult(annotation);

        await using var viewer2 = new PresentationSessionClient(tlsConfiguration);
        viewer2.AnnotationReceived += (_, annotation) => annotation2Tcs.TrySetResult(annotation);

        var presentationHost = new DeviceInfo
        {
            DeviceId = "presentation-host",
            DeviceName = "Presentation Host",
            IPAddress = "127.0.0.1",
            PresentationPort = port,
            SupportsPresentationMode = true,
            PresentationSessionActive = true,
            Type = DeviceType.Desktop
        };

        Assert.True((await viewer1.ConnectAsync(presentationHost, "123456", "viewer-1", "Viewer 1")).Success);
        Assert.True((await viewer2.ConnectAsync(presentationHost, "123456", "viewer-2", "Viewer 2")).Success);
        await WaitForViewerCountAsync(host, 2);

        await viewer1.SendAnnotationAsync(new PresentationAnnotationMessage
        {
            Action = PresentationAnnotationAction.Upsert,
            ChangedByDeviceId = "viewer-1",
            ChangedAtUtc = DateTime.UtcNow,
            Annotation = new PresentationAnnotation
            {
                AnnotationId = "annotation-from-viewer",
                Kind = PresentationAnnotationKind.Freehand,
                CreatedByDeviceId = "viewer-1",
                CreatedAtUtc = DateTime.UtcNow,
                Style = new PresentationAnnotationStyle
                {
                    StrokeColor = "#22C55E",
                    StrokeWidth = 4
                },
                Points =
                [
                    new PresentationAnnotationPoint { X = 0.15, Y = 0.25 },
                    new PresentationAnnotationPoint { X = 0.45, Y = 0.55 },
                    new PresentationAnnotationPoint { X = 0.80, Y = 0.70 }
                ]
            }
        });

        var received1 = await annotation1Tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var received2 = await annotation2Tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal("annotation-from-viewer", received1.Annotation?.AnnotationId);
        Assert.Equal("annotation-from-viewer", received2.Annotation?.AnnotationId);
        Assert.Single(host.GetAnnotations());
        Assert.Equal(PresentationAnnotationKind.Freehand, host.GetAnnotations()[0].Kind);
    }

    [Fact]
    public async Task PresentationSessionHost_SendsExistingAnnotationsToLateJoiners()
    {
        int port = GetFreePort();
        var tlsConfiguration = new TlsConfiguration { Enabled = false };

        await using var host = new PresentationSessionHost(tlsConfiguration);
        await host.StartAsync(port);
        await host.ActivateAsync("123456", "Architecture Review");

        await host.BroadcastAnnotationAsync(new PresentationAnnotationMessage
        {
            Action = PresentationAnnotationAction.Upsert,
            ChangedByDeviceId = "host-presenter",
            ChangedAtUtc = DateTime.UtcNow,
            Annotation = new PresentationAnnotation
            {
                AnnotationId = "persistent-annotation",
                Kind = PresentationAnnotationKind.Rectangle,
                CreatedByDeviceId = "host-presenter",
                CreatedAtUtc = DateTime.UtcNow,
                Style = new PresentationAnnotationStyle
                {
                    StrokeColor = "#F97316",
                    StrokeWidth = 6,
                    Opacity = 0.8
                },
                Points =
                [
                    new PresentationAnnotationPoint { X = 0.2, Y = 0.2 },
                    new PresentationAnnotationPoint { X = 0.7, Y = 0.6 }
                ]
            }
        });

        var annotationTcs = new TaskCompletionSource<PresentationAnnotationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var lateViewer = new PresentationSessionClient(tlsConfiguration);
        lateViewer.AnnotationReceived += (_, annotation) => annotationTcs.TrySetResult(annotation);

        var response = await lateViewer.ConnectAsync(
            new DeviceInfo
            {
                DeviceId = "presentation-host",
                DeviceName = "Presentation Host",
                IPAddress = "127.0.0.1",
                PresentationPort = port,
                SupportsPresentationMode = true,
                PresentationSessionActive = true,
                Type = DeviceType.Desktop
            },
            "123456",
            "late-viewer",
            "Late Viewer");

        Assert.True(response.Success);

        var received = await annotationTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(PresentationAnnotationAction.Upsert, received.Action);
        Assert.Equal("persistent-annotation", received.Annotation?.AnnotationId);
        Assert.Equal(PresentationAnnotationKind.Rectangle, received.Annotation?.Kind);
    }

    [Fact]
    public void PresentationAnnotationBoard_AppliesUpsertsRemovalsAndClear()
    {
        var board = new PresentationAnnotationBoard();
        var changes = new List<PresentationAnnotationMessage>();
        board.Changed += (_, message) => changes.Add(message);

        board.Apply(new PresentationAnnotationMessage
        {
            Action = PresentationAnnotationAction.Upsert,
            ChangedByDeviceId = "viewer-1",
            Annotation = new PresentationAnnotation
            {
                AnnotationId = "anno-1",
                Kind = PresentationAnnotationKind.Text,
                Text = "Focus here",
                CreatedByDeviceId = "viewer-1",
                Style = new PresentationAnnotationStyle
                {
                    StrokeColor = "#2563EB",
                    FontSize = 18
                },
                Points = [new PresentationAnnotationPoint { X = 0.5, Y = 0.4 }]
            }
        });

        Assert.Single(board.GetAnnotations());
        Assert.Equal("Focus here", board.GetAnnotations()[0].Text);

        board.Apply(new PresentationAnnotationMessage
        {
            Action = PresentationAnnotationAction.Remove,
            AnnotationId = "anno-1",
            ChangedByDeviceId = "viewer-1"
        });

        Assert.Empty(board.GetAnnotations());

        board.Apply(new PresentationAnnotationMessage
        {
            Action = PresentationAnnotationAction.Upsert,
            ChangedByDeviceId = "viewer-2",
            Annotation = new PresentationAnnotation
            {
                AnnotationId = "anno-2",
                Kind = PresentationAnnotationKind.Rectangle,
                CreatedByDeviceId = "viewer-2",
                Points =
                [
                    new PresentationAnnotationPoint { X = 0.2, Y = 0.2 },
                    new PresentationAnnotationPoint { X = 0.7, Y = 0.7 }
                ]
            }
        });

        board.Clear("viewer-2");

        Assert.Empty(board.GetAnnotations());
        Assert.Collection(
            changes,
            change => Assert.Equal(PresentationAnnotationAction.Upsert, change.Action),
            change => Assert.Equal(PresentationAnnotationAction.Remove, change.Action),
            change => Assert.Equal(PresentationAnnotationAction.Upsert, change.Action),
            change => Assert.Equal(PresentationAnnotationAction.Clear, change.Action));
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitForViewerCountAsync(PresentationSessionHost host, int expectedCount)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (host.ViewerCount == expectedCount)
                return;

            await Task.Delay(50);
        }

        Assert.Equal(expectedCount, host.ViewerCount);
    }
}
