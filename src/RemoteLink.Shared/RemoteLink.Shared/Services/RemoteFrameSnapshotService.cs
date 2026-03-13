using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

public sealed record RemoteFrameSnapshot(
    byte[] ImageBytes,
    string FileExtension,
    string MimeType,
    DateTime TimestampUtc);

public static class RemoteFrameSnapshotService
{
    private const int BytesPerPixel = 4;
    private static readonly object FrameLock = new();
    private static byte[]? _previousRawFrame;
    private static string? _previousFrameId;
    private static int _previousWidth;
    private static int _previousHeight;

    public static RemoteFrameSnapshot? CreateSnapshot(ScreenData? screenData)
    {
        if (screenData is null)
            return null;

        var renderableFrame = ResolveRenderableFrame(screenData);
        if (renderableFrame is null)
            return null;

        var imageBytes = ScreenFrameConverter.ToImageBytes(renderableFrame);
        if (imageBytes is null)
            return null;

        return new RemoteFrameSnapshot(
            imageBytes,
            GetFileExtension(renderableFrame.Format),
            GetMimeType(renderableFrame.Format),
            renderableFrame.Timestamp == default ? DateTime.UtcNow : renderableFrame.Timestamp.ToUniversalTime());
    }

    public static void ResetFrameCache()
    {
        lock (FrameLock)
        {
            ClearFrameCache();
        }
    }

    public static string BuildFileName(string? remoteDeviceName, RemoteFrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var deviceName = SanitizeSegment(remoteDeviceName);
        return $"RemoteLink_{deviceName}_{snapshot.TimestampUtc:yyyyMMdd_HHmmss_fff}.{snapshot.FileExtension}";
    }

    private static string GetFileExtension(ScreenDataFormat format) =>
        format switch
        {
            ScreenDataFormat.JPEG => "jpg",
            ScreenDataFormat.PNG => "png",
            ScreenDataFormat.Raw => "bmp",
            _ => "png"
        };

    private static string GetMimeType(ScreenDataFormat format) =>
        format switch
        {
            ScreenDataFormat.JPEG => "image/jpeg",
            ScreenDataFormat.PNG => "image/png",
            ScreenDataFormat.Raw => "image/bmp",
            _ => "image/png"
        };

    private static ScreenData? ResolveRenderableFrame(ScreenData screenData)
    {
        lock (FrameLock)
        {
            if (!screenData.IsDelta)
            {
                TrackFrame(screenData);
                return screenData;
            }

            return RebuildDeltaFrame(screenData);
        }
    }

    private static void TrackFrame(ScreenData screenData)
    {
        if (screenData.Format != ScreenDataFormat.Raw)
        {
            ClearFrameCache();
            return;
        }

        var requiredBytes = GetRequiredRawBytes(screenData.Width, screenData.Height);
        if (requiredBytes == 0 || screenData.ImageData.Length < requiredBytes)
        {
            ClearFrameCache();
            return;
        }

        _previousRawFrame = (byte[])screenData.ImageData.Clone();
        _previousFrameId = screenData.FrameId;
        _previousWidth = screenData.Width;
        _previousHeight = screenData.Height;
    }

    private static ScreenData? RebuildDeltaFrame(ScreenData screenData)
    {
        if (screenData.Format != ScreenDataFormat.Raw || screenData.DeltaRegions is null)
        {
            ClearFrameCache();
            return null;
        }

        var requiredBytes = GetRequiredRawBytes(screenData.Width, screenData.Height);
        if (requiredBytes == 0 ||
            _previousRawFrame is null ||
            _previousRawFrame.Length < requiredBytes ||
            _previousWidth != screenData.Width ||
            _previousHeight != screenData.Height ||
            (!string.IsNullOrWhiteSpace(screenData.ReferenceFrameId) &&
             !string.Equals(screenData.ReferenceFrameId, _previousFrameId, StringComparison.Ordinal)))
        {
            ClearFrameCache();
            return null;
        }

        var rebuiltFrameBytes = (byte[])_previousRawFrame.Clone();

        foreach (var region in screenData.DeltaRegions)
        {
            if (region.Width <= 0 ||
                region.Height <= 0 ||
                region.X < 0 ||
                region.Y < 0 ||
                region.X + region.Width > screenData.Width ||
                region.Y + region.Height > screenData.Height)
            {
                ClearFrameCache();
                return null;
            }

            var rowLength = region.Width * BytesPerPixel;
            var expectedDataLength = region.Height * rowLength;
            if (region.DataOffset < 0 ||
                region.DataLength < expectedDataLength ||
                region.DataOffset + expectedDataLength > screenData.ImageData.Length)
            {
                ClearFrameCache();
                return null;
            }

            for (var row = 0; row < region.Height; row++)
            {
                var sourceOffset = region.DataOffset + (row * rowLength);
                var destinationOffset = (((region.Y + row) * screenData.Width) + region.X) * BytesPerPixel;
                Array.Copy(screenData.ImageData, sourceOffset, rebuiltFrameBytes, destinationOffset, rowLength);
            }
        }

        var rebuiltFrame = new ScreenData
        {
            FrameId = screenData.FrameId,
            Timestamp = screenData.Timestamp,
            ImageData = rebuiltFrameBytes,
            Width = screenData.Width,
            Height = screenData.Height,
            Format = ScreenDataFormat.Raw,
            Quality = screenData.Quality
        };

        TrackFrame(rebuiltFrame);
        return rebuiltFrame;
    }

    private static int GetRequiredRawBytes(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return 0;

        var requiredBytes = (long)width * height * BytesPerPixel;
        return requiredBytes > int.MaxValue ? 0 : (int)requiredBytes;
    }

    private static void ClearFrameCache()
    {
        _previousRawFrame = null;
        _previousFrameId = null;
        _previousWidth = 0;
        _previousHeight = 0;
    }

    private static string SanitizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "RemoteHost";

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Trim()
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray());

        sanitized = sanitized.Replace(' ', '_');
        return string.IsNullOrWhiteSpace(sanitized) ? "RemoteHost" : sanitized;
    }
}
