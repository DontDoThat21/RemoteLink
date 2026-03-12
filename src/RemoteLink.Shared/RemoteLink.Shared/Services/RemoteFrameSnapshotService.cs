using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

public sealed record RemoteFrameSnapshot(
    byte[] ImageBytes,
    string FileExtension,
    string MimeType,
    DateTime TimestampUtc);

public static class RemoteFrameSnapshotService
{
    public static RemoteFrameSnapshot? CreateSnapshot(ScreenData? screenData)
    {
        if (screenData is null)
            return null;

        var imageBytes = ScreenFrameConverter.ToImageBytes(screenData);
        if (imageBytes is null)
            return null;

        return new RemoteFrameSnapshot(
            imageBytes,
            GetFileExtension(screenData.Format),
            GetMimeType(screenData.Format),
            screenData.Timestamp == default ? DateTime.UtcNow : screenData.Timestamp.ToUniversalTime());
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
