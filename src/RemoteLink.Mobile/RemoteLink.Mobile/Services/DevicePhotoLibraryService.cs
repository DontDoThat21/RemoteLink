namespace RemoteLink.Mobile.Services;

public sealed class DevicePhotoLibraryService : IDevicePhotoLibraryService
{
    public async Task<string> SaveImageAsync(byte[] imageBytes, string fileName, string mimeType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        if (imageBytes.Length == 0)
            throw new ArgumentException("Image data cannot be empty.", nameof(imageBytes));

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("A file name is required.", nameof(fileName));

        mimeType = string.IsNullOrWhiteSpace(mimeType) ? "image/png" : mimeType;

#if ANDROID
        return await SaveToAndroidMediaStoreAsync(imageBytes, fileName, mimeType, cancellationToken);
#else
        return await SaveToPicturesDirectoryAsync(imageBytes, fileName, cancellationToken);
#endif
    }

#if ANDROID
    private static async Task<string> SaveToAndroidMediaStoreAsync(byte[] imageBytes, string fileName, string mimeType, CancellationToken cancellationToken)
    {
        var resolver = Android.App.Application.Context?.ContentResolver
            ?? throw new InvalidOperationException("Android content resolver is unavailable.");

        using var values = new Android.Content.ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, mimeType);

        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q)
        {
            values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, $"{Android.OS.Environment.DirectoryPictures}/RemoteLink");
            values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 1);
        }

        var collection = Android.Provider.MediaStore.Images.Media.GetContentUri(Android.Provider.MediaStore.VolumeExternalPrimary);
        var uri = resolver.Insert(collection, values)
            ?? throw new IOException("Unable to create a gallery entry for the screenshot.");

        try
        {
            await using var outputStream = resolver.OpenOutputStream(uri)
                ?? throw new IOException("Unable to open the gallery destination stream.");

            await outputStream.WriteAsync(imageBytes.AsMemory(), cancellationToken);
            await outputStream.FlushAsync(cancellationToken);

            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q)
            {
                using var completedValues = new Android.Content.ContentValues();
                completedValues.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 0);
                resolver.Update(uri, completedValues, null, null);
            }

            return uri.ToString();
        }
        catch
        {
            resolver.Delete(uri, null, null);
            throw;
        }
    }
#endif

    private static async Task<string> SaveToPicturesDirectoryAsync(byte[] imageBytes, string fileName, CancellationToken cancellationToken)
    {
        var rootDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(rootDirectory))
            rootDirectory = Path.Combine(FileSystem.Current.AppDataDirectory, "Screenshots");
        else
            rootDirectory = Path.Combine(rootDirectory, "RemoteLink");

        Directory.CreateDirectory(rootDirectory);

        var fullPath = Path.Combine(rootDirectory, fileName);
        var uniquePath = GetUniquePath(fullPath);
        await File.WriteAllBytesAsync(uniquePath, imageBytes, cancellationToken);
        return uniquePath;
    }

    private static string GetUniquePath(string fullPath)
    {
        if (!File.Exists(fullPath))
            return fullPath;

        var directory = Path.GetDirectoryName(fullPath) ?? FileSystem.Current.AppDataDirectory;
        var fileName = Path.GetFileNameWithoutExtension(fullPath);
        var extension = Path.GetExtension(fullPath);

        for (var counter = 2; counter < 10_000; counter++)
        {
            var candidate = Path.Combine(directory, $"{fileName}_{counter}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, $"{fileName}_{Guid.NewGuid():N}{extension}");
    }
}
