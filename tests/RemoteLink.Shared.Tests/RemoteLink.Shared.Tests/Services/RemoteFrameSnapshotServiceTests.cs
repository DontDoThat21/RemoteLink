using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests.Services;

public class RemoteFrameSnapshotServiceTests
{
    [Fact]
    public void CreateSnapshot_RawFrame_ReturnsBmpSnapshot()
    {
        var frame = new ScreenData
        {
            Width = 2,
            Height = 2,
            Format = ScreenDataFormat.Raw,
            Timestamp = new DateTime(2026, 3, 12, 10, 15, 30, DateTimeKind.Utc),
            ImageData = Enumerable.Repeat((byte)0x44, 16).ToArray()
        };

        var snapshot = RemoteFrameSnapshotService.CreateSnapshot(frame);

        Assert.NotNull(snapshot);
        Assert.Equal("bmp", snapshot!.FileExtension);
        Assert.Equal("image/bmp", snapshot.MimeType);
        Assert.Equal((byte)'B', snapshot.ImageBytes[0]);
        Assert.Equal((byte)'M', snapshot.ImageBytes[1]);
    }

    [Fact]
    public void CreateSnapshot_PngFrame_ReturnsPassThroughSnapshot()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var frame = new ScreenData
        {
            Width = 10,
            Height = 10,
            Format = ScreenDataFormat.PNG,
            ImageData = bytes
        };

        var snapshot = RemoteFrameSnapshotService.CreateSnapshot(frame);

        Assert.NotNull(snapshot);
        Assert.Equal("png", snapshot!.FileExtension);
        Assert.Equal("image/png", snapshot.MimeType);
        Assert.Same(bytes, snapshot.ImageBytes);
    }

    [Fact]
    public void BuildFileName_SanitizesRemoteDeviceName()
    {
        var snapshot = new RemoteFrameSnapshot(Array.Empty<byte>(), "jpg", "image/jpeg", new DateTime(2026, 3, 12, 21, 4, 5, 123, DateTimeKind.Utc));

        var fileName = RemoteFrameSnapshotService.BuildFileName("Office / Host:01", snapshot);

        Assert.Equal("RemoteLink_Office___Host_01_20260312_210405_123.jpg", fileName);
    }
}
