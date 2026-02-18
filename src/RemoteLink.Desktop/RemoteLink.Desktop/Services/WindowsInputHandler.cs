using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Desktop.Services;

/// <summary>
/// Windows-specific input handler that uses user32.dll SendInput P/Invoke
/// to simulate real mouse and keyboard events on the host machine.
/// </summary>
public class WindowsInputHandler : IInputHandler
{
    private readonly ILogger<WindowsInputHandler> _logger;
    private bool _isActive;

    public WindowsInputHandler(ILogger<WindowsInputHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsActive => _isActive;

    /// <inheritdoc/>
    public Task StartAsync()
    {
        _isActive = true;
        _logger.LogInformation("WindowsInputHandler started.");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync()
    {
        _isActive = false;
        _logger.LogInformation("WindowsInputHandler stopped.");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task ProcessInputEventAsync(InputEvent inputEvent)
    {
        if (!_isActive)
        {
            _logger.LogWarning("ProcessInputEventAsync called while inactive — ignoring.");
            return;
        }

        _logger.LogDebug("Processing {Type} event at ({X},{Y})", inputEvent.Type, inputEvent.X, inputEvent.Y);

        try
        {
            switch (inputEvent.Type)
            {
                case InputEventType.MouseMove:
                    SimulateMouseMove(inputEvent.X, inputEvent.Y);
                    break;

                case InputEventType.MouseClick:
                    SimulateMouseClick(inputEvent.X, inputEvent.Y, inputEvent.IsPressed);
                    break;

                case InputEventType.MouseWheel:
                    SimulateMouseWheel(inputEvent.Y);
                    break;

                case InputEventType.KeyPress:
                    SimulateKey(inputEvent.KeyCode, pressed: true);
                    break;

                case InputEventType.KeyRelease:
                    SimulateKey(inputEvent.KeyCode, pressed: false);
                    break;

                case InputEventType.TextInput:
                    SimulateTextInput(inputEvent.Text);
                    break;

                default:
                    _logger.LogWarning("Unhandled input event type: {Type}", inputEvent.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing input event of type {Type}", inputEvent.Type);
        }

        await Task.CompletedTask;
    }

    // ── Mouse helpers ──────────────────────────────────────────────────────────

    private void SimulateMouseMove(int x, int y)
    {
        if (!OperatingSystem.IsWindows()) return;

        // Convert to absolute normalised coordinates (0–65535)
        int screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int screenH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);

        int absX = screenW > 0 ? x * 65535 / screenW : x;
        int absY = screenH > 0 ? y * 65535 / screenH : y;

        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            union = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE
                }
            }
        };

        SendInputInternal(input);
    }

    private void SimulateMouseClick(int x, int y, bool pressed)
    {
        if (!OperatingSystem.IsWindows()) return;

        // First move to position
        SimulateMouseMove(x, y);

        var flags = pressed
            ? NativeMethods.MOUSEEVENTF_LEFTDOWN
            : NativeMethods.MOUSEEVENTF_LEFTUP;

        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            union = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT { dwFlags = flags }
            }
        };

        SendInputInternal(input);
    }

    private void SimulateMouseWheel(int delta)
    {
        if (!OperatingSystem.IsWindows()) return;

        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            union = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    mouseData = delta * NativeMethods.WHEEL_DELTA,
                    dwFlags = NativeMethods.MOUSEEVENTF_WHEEL
                }
            }
        };

        SendInputInternal(input);
    }

    // ── Keyboard helpers ───────────────────────────────────────────────────────

    private void SimulateKey(string? keyCode, bool pressed)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(keyCode)) return;

        if (!Enum.TryParse<NativeMethods.VirtualKey>(keyCode, ignoreCase: true, out var vk))
        {
            _logger.LogWarning("Unknown virtual key code: {KeyCode}", keyCode);
            return;
        }

        var flags = pressed
            ? NativeMethods.KEYEVENTF_NONE
            : NativeMethods.KEYEVENTF_KEYUP;

        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            union = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    dwFlags = flags
                }
            }
        };

        SendInputInternal(input);
    }

    private void SimulateTextInput(string? text)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(text)) return;

        var inputs = new List<NativeMethods.INPUT>();

        foreach (char c in text)
        {
            // Key down
            inputs.Add(new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                union = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wScan = c,
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE
                    }
                }
            });

            // Key up
            inputs.Add(new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                union = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wScan = c,
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP
                    }
                }
            });
        }

        var arr = inputs.ToArray();
        uint sent = NativeMethods.SendInput((uint)arr.Length, arr, NativeMethods.INPUT.Size);

        if (sent != arr.Length)
            _logger.LogWarning("SendInput sent {Sent}/{Total} text events", sent, arr.Length);
    }

    private void SendInputInternal(NativeMethods.INPUT input)
    {
        uint sent = NativeMethods.SendInput(1, new[] { input }, NativeMethods.INPUT.Size);
        if (sent == 0)
        {
            int err = Marshal.GetLastWin32Error();
            _logger.LogWarning("SendInput returned 0 (Win32 error: {Error})", err);
        }
    }
}

/// <summary>P/Invoke declarations for user32.dll input simulation.</summary>
internal static class NativeMethods
{
    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;

    // Mouse flags
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    // Keyboard flags
    public const uint KEYEVENTF_NONE = 0x0000;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const uint KEYEVENTF_SCANCODE = 0x0008;

    public const int WHEEL_DELTA = 120;

    // GetSystemMetrics indices
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion union;

        public static int Size => Marshal.SizeOf(typeof(INPUT));
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    /// <summary>Common Windows virtual key codes.</summary>
    public enum VirtualKey : ushort
    {
        // Control keys
        Back = 0x08,
        Tab = 0x09,
        Return = 0x0D,
        Shift = 0x10,
        Control = 0x11,
        Menu = 0x12,       // Alt
        Pause = 0x13,
        Capital = 0x14,    // Caps Lock
        Escape = 0x1B,
        Space = 0x20,
        Prior = 0x21,      // Page Up
        Next = 0x22,       // Page Down
        End = 0x23,
        Home = 0x24,
        Left = 0x25,
        Up = 0x26,
        Right = 0x27,
        Down = 0x28,
        Snapshot = 0x2C,   // Print Screen
        Insert = 0x2D,
        Delete = 0x2E,

        // Letters A–Z
        A = 0x41, B = 0x42, C = 0x43, D = 0x44, E = 0x45,
        F = 0x46, G = 0x47, H = 0x48, I = 0x49, J = 0x4A,
        K = 0x4B, L = 0x4C, M = 0x4D, N = 0x4E, O = 0x4F,
        P = 0x50, Q = 0x51, R = 0x52, S = 0x53, T = 0x54,
        U = 0x55, V = 0x56, W = 0x57, X = 0x58, Y = 0x59,
        Z = 0x5A,

        // Digits 0–9
        D0 = 0x30, D1 = 0x31, D2 = 0x32, D3 = 0x33, D4 = 0x34,
        D5 = 0x35, D6 = 0x36, D7 = 0x37, D8 = 0x38, D9 = 0x39,

        // Function keys
        F1 = 0x70, F2 = 0x71, F3 = 0x72, F4 = 0x73,
        F5 = 0x74, F6 = 0x75, F7 = 0x76, F8 = 0x77,
        F9 = 0x78, F10 = 0x79, F11 = 0x7A, F12 = 0x7B,

        // Numpad
        Numpad0 = 0x60, Numpad1 = 0x61, Numpad2 = 0x62, Numpad3 = 0x63,
        Numpad4 = 0x64, Numpad5 = 0x65, Numpad6 = 0x66, Numpad7 = 0x67,
        Numpad8 = 0x68, Numpad9 = 0x69,

        // Modifier aliases (common names)
        LShift = 0xA0, RShift = 0xA1,
        LControl = 0xA2, RControl = 0xA3,
        LMenu = 0xA4, RMenu = 0xA5, // Alt keys
        LWin = 0x5B, RWin = 0x5C,

        // OEM
        OemSemicolon = 0xBA,
        OemPlus = 0xBB,
        OemComma = 0xBC,
        OemMinus = 0xBD,
        OemPeriod = 0xBE,
        OemQuestion = 0xBF,
        OemTilde = 0xC0,
        OemOpenBrackets = 0xDB,
        OemPipe = 0xDC,
        OemCloseBrackets = 0xDD,
        OemQuotes = 0xDE,
    }
}
