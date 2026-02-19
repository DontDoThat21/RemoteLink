using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;
using Microsoft.Extensions.Logging;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Windows implementation of clipboard monitoring and management using Win32 API.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class WindowsClipboardService : IClipboardService
{
    private readonly ILogger<WindowsClipboardService> _logger;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private bool _isMonitoring;
    private string? _lastText;
    private byte[]? _lastImageHash;

    public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;

    public bool IsMonitoring => _isMonitoring;

    public WindowsClipboardService(ILogger<WindowsClipboardService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_isMonitoring)
            {
                _logger.LogDebug("Clipboard monitoring already started");
                return Task.CompletedTask;
            }

            _cts = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorClipboardAsync(_cts.Token), cancellationToken);
            _isMonitoring = true;
            _logger.LogInformation("Clipboard monitoring started");
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_isMonitoring)
            {
                _logger.LogDebug("Clipboard monitoring already stopped");
                return;
            }

            _cts?.Cancel();
            _isMonitoring = false;
        }

        if (_monitorTask != null)
        {
            try
            {
                await _monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _cts?.Dispose();
        _cts = null;
        _monitorTask = null;
        _lastText = null;
        _lastImageHash = null;

        _logger.LogInformation("Clipboard monitoring stopped");
    }

    private async Task MonitorClipboardAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    await CheckClipboardChangesAsync(cancellationToken);
                }

                // Poll every 500ms
                await Task.Delay(500, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error monitoring clipboard");
                await Task.Delay(1000, cancellationToken); // Back off on error
            }
        }
    }

    private async Task CheckClipboardChangesAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return;

        // Check for text
        if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT))
        {
            var text = await GetTextAsync(cancellationToken);
            if (text != null && text != _lastText)
            {
                _lastText = text;
                _lastImageHash = null; // Clear image hash when text changes
                OnClipboardChanged(new ClipboardChangedEventArgs
                {
                    ContentType = ClipboardContentType.Text,
                    Text = text
                });
            }
        }
        // Check for image (only if no text)
        else if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_DIB) ||
                 NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_BITMAP))
        {
            var imageData = await GetImageAsync(cancellationToken);
            if (imageData != null)
            {
                var hash = ComputeSimpleHash(imageData);
                if (hash != _lastImageHash)
                {
                    _lastImageHash = hash;
                    _lastText = null; // Clear text when image changes
                    OnClipboardChanged(new ClipboardChangedEventArgs
                    {
                        ContentType = ClipboardContentType.Image,
                        ImageData = imageData
                    });
                }
            }
        }
    }

    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("GetTextAsync called on non-Windows platform");
            return Task.FromResult<string?>(null);
        }

        try
        {
            if (!NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                return Task.FromResult<string?>(null);
            }

            try
            {
                IntPtr handle = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
                if (handle == IntPtr.Zero)
                {
                    return Task.FromResult<string?>(null);
                }

                IntPtr textPtr = NativeMethods.GlobalLock(handle);
                if (textPtr == IntPtr.Zero)
                {
                    return Task.FromResult<string?>(null);
                }

                try
                {
                    string? text = Marshal.PtrToStringUni(textPtr);
                    return Task.FromResult(text);
                }
                finally
                {
                    NativeMethods.GlobalUnlock(handle);
                }
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting clipboard text");
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("SetTextAsync called on non-Windows platform");
            return Task.CompletedTask;
        }

        if (string.IsNullOrEmpty(text))
        {
            return Task.CompletedTask;
        }

        try
        {
            if (!NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                _logger.LogWarning("Failed to open clipboard");
                return Task.CompletedTask;
            }

            try
            {
                NativeMethods.EmptyClipboard();

                IntPtr hGlobal = Marshal.StringToHGlobalUni(text);
                if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(hGlobal);
                    _logger.LogWarning("Failed to set clipboard data");
                }
                else
                {
                    _lastText = text; // Update cache to avoid re-triggering event
                }
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting clipboard text");
        }

        return Task.CompletedTask;
    }

    public Task<byte[]?> GetImageAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("GetImageAsync called on non-Windows platform");
            return Task.FromResult<byte[]?>(null);
        }

        // For simplicity, return null for now
        // Full implementation would use CF_DIB/CF_BITMAP and convert to PNG
        _logger.LogDebug("GetImageAsync not yet fully implemented");
        return Task.FromResult<byte[]?>(null);
    }

    public Task SetImageAsync(byte[] pngData, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("SetImageAsync called on non-Windows platform");
            return Task.CompletedTask;
        }

        // For simplicity, do nothing for now
        // Full implementation would decode PNG and set as CF_DIB/CF_BITMAP
        _logger.LogDebug("SetImageAsync not yet fully implemented");
        return Task.CompletedTask;
    }

    private void OnClipboardChanged(ClipboardChangedEventArgs e)
    {
        try
        {
            ClipboardChanged?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in clipboard changed event handler");
        }
    }

    private static byte[]? ComputeSimpleHash(byte[] data)
    {
        if (data.Length < 16) return data;
        
        // Simple hash: first 8 bytes + last 8 bytes
        var hash = new byte[16];
        Array.Copy(data, 0, hash, 0, 8);
        Array.Copy(data, data.Length - 8, hash, 8, 8);
        return hash;
    }

    private static partial class NativeMethods
    {
        public const uint CF_TEXT = 1;
        public const uint CF_BITMAP = 2;
        public const uint CF_DIB = 8;
        public const uint CF_UNICODETEXT = 13;

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool OpenClipboard(IntPtr hWndNewOwner);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseClipboard();

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool EmptyClipboard();

        [LibraryImport("user32.dll", SetLastError = true)]
        public static partial IntPtr GetClipboardData(uint uFormat);

        [LibraryImport("user32.dll", SetLastError = true)]
        public static partial IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsClipboardFormatAvailable(uint format);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        public static partial IntPtr GlobalLock(IntPtr hMem);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GlobalUnlock(IntPtr hMem);
    }
}
