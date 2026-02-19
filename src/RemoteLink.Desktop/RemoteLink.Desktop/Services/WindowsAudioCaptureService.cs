using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Windows-specific audio capture using WASAPI (Windows Audio Session API)
/// Captures system loopback audio (what's playing on the speakers)
/// </summary>
[SupportedOSPlatform("windows")]
public partial class WindowsAudioCaptureService : IAudioCaptureService
{
    private readonly ILogger<WindowsAudioCaptureService> _logger;
    private AudioCaptureSettings _settings = new();
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private readonly object _lock = new();

    public bool IsCapturing { get; private set; }
    public AudioCaptureSettings Settings => _settings;

    public event EventHandler<AudioData>? AudioCaptured;

    public WindowsAudioCaptureService(ILogger<WindowsAudioCaptureService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("WindowsAudioCaptureService called on non-Windows platform");
            return Task.CompletedTask;
        }

        lock (_lock)
        {
            if (IsCapturing)
            {
                _logger.LogWarning("Audio capture already started");
                return Task.CompletedTask;
            }

            _captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _captureTask = Task.Run(() => CaptureAudioLoop(_captureCts.Token), _captureCts.Token);
            IsCapturing = true;
            _logger.LogInformation("Audio capture started (Sample Rate: {Rate}Hz, Channels: {Channels}, Chunk: {Duration}ms)",
                _settings.SampleRate, _settings.Channels, _settings.ChunkDurationMs);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? taskToWait;
        lock (_lock)
        {
            if (!IsCapturing)
            {
                return;
            }

            _captureCts?.Cancel();
            taskToWait = _captureTask;
            IsCapturing = false;
        }

        if (taskToWait != null)
        {
            try
            {
                await taskToWait.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation
            }
        }

        _captureCts?.Dispose();
        _captureCts = null;
        _captureTask = null;
        _logger.LogInformation("Audio capture stopped");
    }

    public void UpdateSettings(AudioCaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_lock)
        {
            _settings = settings;
            _logger.LogDebug("Audio settings updated: {Rate}Hz, {Channels}ch, {Bits}bit",
                settings.SampleRate, settings.Channels, settings.BitsPerSample);
        }
    }

    private async Task CaptureAudioLoop(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            // Initialize COM for this thread
            CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);

            IntPtr enumerator = IntPtr.Zero;
            IntPtr device = IntPtr.Zero;
            IntPtr audioClient = IntPtr.Zero;
            IntPtr captureClient = IntPtr.Zero;

            try
            {
                // Get default audio endpoint (speakers for loopback)
                var clsid = CLSID_MMDeviceEnumerator;
                var iid = IID_IMMDeviceEnumerator;
                var hr = CoCreateInstance(
                    ref clsid,
                    IntPtr.Zero,
                    CLSCTX_ALL,
                    ref iid,
                    out enumerator);

                if (hr != 0 || enumerator == IntPtr.Zero)
                {
                    _logger.LogError("Failed to create device enumerator (HRESULT: 0x{HR:X8})", hr);
                    return;
                }

                var enumeratorVtbl = Marshal.PtrToStructure<IMMDeviceEnumeratorVtbl>(
                    Marshal.ReadIntPtr(enumerator));

                hr = enumeratorVtbl.GetDefaultAudioEndpoint(
                    enumerator,
                    EDataFlow.eRender, // Speakers (for loopback)
                    ERole.eConsole,
                    out device);

                if (hr != 0 || device == IntPtr.Zero)
                {
                    _logger.LogError("Failed to get default audio endpoint (HRESULT: 0x{HR:X8})", hr);
                    return;
                }

                // Activate audio client
                var deviceVtbl = Marshal.PtrToStructure<IMMDeviceVtbl>(
                    Marshal.ReadIntPtr(device));

                var iidAudioClient = IID_IAudioClient;
                hr = deviceVtbl.Activate(
                    device,
                    ref iidAudioClient,
                    CLSCTX_ALL,
                    IntPtr.Zero,
                    out audioClient);

                if (hr != 0 || audioClient == IntPtr.Zero)
                {
                    _logger.LogError("Failed to activate audio client (HRESULT: 0x{HR:X8})", hr);
                    return;
                }

                // Get mix format
                var audioClientVtbl = Marshal.PtrToStructure<IAudioClientVtbl>(
                    Marshal.ReadIntPtr(audioClient));

                hr = audioClientVtbl.GetMixFormat(audioClient, out IntPtr formatPtr);

                if (hr != 0 || formatPtr == IntPtr.Zero)
                {
                    _logger.LogError("Failed to get mix format (HRESULT: 0x{HR:X8})", hr);
                    return;
                }

                var format = Marshal.PtrToStructure<WAVEFORMATEX>(formatPtr);
                _logger.LogDebug("Audio format: {Rate}Hz, {Channels}ch, {Bits}bit",
                    format.nSamplesPerSec, format.nChannels, format.wBitsPerSample);

                // Initialize audio client for loopback capture
                const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
                long bufferDuration = 10000000; // 1 second in 100ns units

                hr = audioClientVtbl.Initialize(
                    audioClient,
                    AUDCLNT_SHAREMODE_SHARED,
                    AUDCLNT_STREAMFLAGS_LOOPBACK,
                    bufferDuration,
                    0,
                    formatPtr,
                    IntPtr.Zero);

                if (hr != 0)
                {
                    _logger.LogError("Failed to initialize audio client (HRESULT: 0x{HR:X8})", hr);
                    CoTaskMemFree(formatPtr);
                    return;
                }

                CoTaskMemFree(formatPtr);

                // Get capture client
                var iidCaptureClient = IID_IAudioCaptureClient;
                hr = audioClientVtbl.GetService(
                    audioClient,
                    ref iidCaptureClient,
                    out captureClient);

                if (hr != 0 || captureClient == IntPtr.Zero)
                {
                    _logger.LogError("Failed to get capture client (HRESULT: 0x{HR:X8})", hr);
                    return;
                }

                var captureClientVtbl = Marshal.PtrToStructure<IAudioCaptureClientVtbl>(
                    Marshal.ReadIntPtr(captureClient));

                // Start audio client
                hr = audioClientVtbl.Start(audioClient);
                if (hr != 0)
                {
                    _logger.LogError("Failed to start audio client (HRESULT: 0x{HR:X8})", hr);
                    return;
                }

                _logger.LogInformation("WASAPI audio capture initialized successfully");

                // Capture loop
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(_settings.ChunkDurationMs, cancellationToken).ConfigureAwait(false);

                    // Get available packet count
                    hr = captureClientVtbl.GetNextPacketSize(captureClient, out uint packetSize);
                    if (hr != 0)
                    {
                        _logger.LogWarning("GetNextPacketSize failed (HRESULT: 0x{HR:X8})", hr);
                        continue;
                    }

                    while (packetSize > 0)
                    {
                        // Get buffer
                        hr = captureClientVtbl.GetBuffer(
                            captureClient,
                            out IntPtr bufferPtr,
                            out uint numFrames,
                            out uint flags,
                            out ulong position,
                            out ulong timestamp);

                        if (hr != 0 || bufferPtr == IntPtr.Zero)
                        {
                            break;
                        }

                        // Calculate buffer size
                        int bytesPerFrame = format.nChannels * (format.wBitsPerSample / 8);
                        int bufferSize = (int)numFrames * bytesPerFrame;

                        // Copy audio data
                        byte[] audioData = new byte[bufferSize];
                        Marshal.Copy(bufferPtr, audioData, 0, bufferSize);

                        // Release buffer
                        captureClientVtbl.ReleaseBuffer(captureClient, numFrames);

                        // Fire event with captured audio
                        if (AudioCaptured != null && audioData.Length > 0)
                        {
                            var data = new AudioData
                            {
                                Data = audioData,
                                SampleRate = (int)format.nSamplesPerSec,
                                Channels = format.nChannels,
                                BitsPerSample = format.wBitsPerSample,
                                Timestamp = DateTime.UtcNow,
                                DurationMs = (int)(numFrames * 1000 / format.nSamplesPerSec),
                                Format = "PCM"
                            };

                            AudioCaptured.Invoke(this, data);
                        }

                        // Get next packet size
                        hr = captureClientVtbl.GetNextPacketSize(captureClient, out packetSize);
                        if (hr != 0)
                        {
                            break;
                        }
                    }
                }

                // Stop audio client
                audioClientVtbl.Stop(audioClient);
            }
            finally
            {
                // Release COM objects
                if (captureClient != IntPtr.Zero) Marshal.Release(captureClient);
                if (audioClient != IntPtr.Zero) Marshal.Release(audioClient);
                if (device != IntPtr.Zero) Marshal.Release(device);
                if (enumerator != IntPtr.Zero) Marshal.Release(enumerator);

                CoUninitialize();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Audio capture cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in audio capture loop");
        }
        finally
        {
            // Ensure IsCapturing is set to false when loop exits
            lock (_lock)
            {
                IsCapturing = false;
            }
        }
    }

    // ── COM Interop ───────────────────────────────────────────────────────────

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        ref Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        out IntPtr ppv);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(IntPtr pv);

    private const uint COINIT_MULTITHREADED = 0;
    private const uint CLSCTX_ALL = 23;
    private const int AUDCLNT_SHAREMODE_SHARED = 0;

    private static Guid CLSID_MMDeviceEnumerator => new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static Guid IID_IMMDeviceEnumerator => new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static Guid IID_IAudioClient => new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static Guid IID_IAudioCaptureClient => new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

    private enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2
    }

    private enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IMMDeviceEnumeratorVtbl
    {
        public IntPtr QueryInterface;
        public IntPtr AddRef;
        public IntPtr Release;
        public IntPtr EnumAudioEndpoints;
        public GetDefaultAudioEndpointDelegate GetDefaultAudioEndpoint;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int GetDefaultAudioEndpointDelegate(
            IntPtr self,
            EDataFlow dataFlow,
            ERole role,
            out IntPtr device);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IMMDeviceVtbl
    {
        public IntPtr QueryInterface;
        public IntPtr AddRef;
        public IntPtr Release;
        public ActivateDelegate Activate;
        public IntPtr OpenPropertyStore;
        public IntPtr GetId;
        public IntPtr GetState;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int ActivateDelegate(
            IntPtr self,
            ref Guid iid,
            uint dwClsCtx,
            IntPtr pActivationParams,
            out IntPtr ppInterface);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IAudioClientVtbl
    {
        public IntPtr QueryInterface;
        public IntPtr AddRef;
        public IntPtr Release;
        public InitializeDelegate Initialize;
        public IntPtr GetBufferSize;
        public IntPtr GetStreamLatency;
        public IntPtr GetCurrentPadding;
        public IntPtr IsFormatSupported;
        public GetMixFormatDelegate GetMixFormat;
        public IntPtr GetDevicePeriod;
        public StartDelegate Start;
        public StopDelegate Stop;
        public IntPtr Reset;
        public IntPtr SetEventHandle;
        public GetServiceDelegate GetService;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int InitializeDelegate(
            IntPtr self,
            int shareMode,
            int streamFlags,
            long bufferDuration,
            long periodicity,
            IntPtr format,
            IntPtr sessionGuid);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int GetMixFormatDelegate(
            IntPtr self,
            out IntPtr format);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int StartDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int StopDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int GetServiceDelegate(
            IntPtr self,
            ref Guid riid,
            out IntPtr ppv);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IAudioCaptureClientVtbl
    {
        public IntPtr QueryInterface;
        public IntPtr AddRef;
        public IntPtr Release;
        public GetBufferDelegate GetBuffer;
        public ReleaseBufferDelegate ReleaseBuffer;
        public GetNextPacketSizeDelegate GetNextPacketSize;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int GetBufferDelegate(
            IntPtr self,
            out IntPtr data,
            out uint numFrames,
            out uint flags,
            out ulong position,
            out ulong timestamp);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int ReleaseBufferDelegate(
            IntPtr self,
            uint numFrames);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int GetNextPacketSizeDelegate(
            IntPtr self,
            out uint packetSize);
    }
}
