namespace RemoteLink.Mobile.Services;

public interface IDevicePhotoLibraryService
{
    Task<string> SaveImageAsync(byte[] imageBytes, string fileName, string mimeType, CancellationToken cancellationToken = default);
}
