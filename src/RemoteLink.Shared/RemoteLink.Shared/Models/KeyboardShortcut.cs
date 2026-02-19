namespace RemoteLink.Shared.Models;

/// <summary>
/// Common keyboard shortcuts for remote desktop control.
/// Note: Some shortcuts (e.g., Ctrl+Alt+Del) cannot be simulated on Windows due to security restrictions.
/// </summary>
public enum KeyboardShortcut
{
    /// <summary>
    /// Windows key + D — Show desktop / minimize all windows
    /// </summary>
    ShowDesktop,

    /// <summary>
    /// Windows key + L — Lock workstation
    /// </summary>
    LockWorkstation,

    /// <summary>
    /// Alt + Tab — Task switcher
    /// </summary>
    TaskSwitcher,

    /// <summary>
    /// Alt + F4 — Close active window
    /// </summary>
    CloseWindow,

    /// <summary>
    /// Windows key + R — Run dialog
    /// </summary>
    RunDialog,

    /// <summary>
    /// Windows key + E — Open File Explorer
    /// </summary>
    Explorer,

    /// <summary>
    /// Ctrl + Alt + Delete — Secure Attention Sequence (SAS)
    /// NOTE: Cannot be simulated via SendInput on Windows for security reasons.
    /// This shortcut may be ignored or require special handling.
    /// </summary>
    CtrlAltDelete,

    /// <summary>
    /// Windows key + Tab — Task view / virtual desktop switcher
    /// </summary>
    TaskView,

    /// <summary>
    /// Ctrl + Shift + Esc — Task Manager
    /// </summary>
    TaskManager,

    /// <summary>
    /// Windows key + I — Settings
    /// </summary>
    Settings,

    /// <summary>
    /// Alt + Enter — Toggle fullscreen (context-dependent)
    /// </summary>
    ToggleFullscreen,

    /// <summary>
    /// Windows key + Left Arrow — Snap window to left half of screen
    /// </summary>
    SnapLeft,

    /// <summary>
    /// Windows key + Right Arrow — Snap window to right half of screen
    /// </summary>
    SnapRight,

    /// <summary>
    /// Windows key + Up Arrow — Maximize window
    /// </summary>
    MaximizeWindow,

    /// <summary>
    /// Windows key + Down Arrow — Minimize/restore window
    /// </summary>
    MinimizeWindow,
}
