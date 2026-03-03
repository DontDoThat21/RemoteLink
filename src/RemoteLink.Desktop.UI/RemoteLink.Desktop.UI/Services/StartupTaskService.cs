using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace RemoteLink.Desktop.UI.Services;

/// <summary>
/// Manages Windows auto-start registration for RemoteLink.
/// Supports both MSIX packaged (StartupTask API) and unpackaged (registry) modes.
/// </summary>
public class StartupTaskService
{
    private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "RemoteLink";
    private const string StartupTaskId = "RemoteLinkStartup";

    /// <summary>
    /// Returns true if the app is currently registered to start with Windows.
    /// </summary>
    public async Task<bool> IsEnabledAsync()
    {
        if (IsPackagedApp())
        {
            try
            {
                var startupTask = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
                return startupTask.State == Windows.ApplicationModel.StartupTaskState.Enabled;
            }
            catch
            {
                return false;
            }
        }
        else
        {
            return IsRegistryKeyPresent();
        }
    }

    /// <summary>
    /// Enables or disables Windows startup registration.
    /// </summary>
    public async Task<bool> SetEnabledAsync(bool enable)
    {
        if (IsPackagedApp())
        {
            return await SetPackagedStartupAsync(enable);
        }
        else
        {
            return SetRegistryStartup(enable);
        }
    }

    // ── MSIX packaged mode ──────────────────────────────────────────────────

    private static async Task<bool> SetPackagedStartupAsync(bool enable)
    {
        try
        {
            var startupTask = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);

            if (enable)
            {
                var result = await startupTask.RequestEnableAsync();
                // EnabledByPolicy or Enabled both count as success
                return result == Windows.ApplicationModel.StartupTaskState.Enabled ||
                       result == Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            }
            else
            {
                startupTask.Disable();
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    // ── Unpackaged mode (registry) ──────────────────────────────────────────

    private static bool IsRegistryKeyPresent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool SetRegistryStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
            if (key == null) return false;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return false;
                key.SetValue(AppName, $"\"{exePath}\" --minimized");
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Detect whether we're running as an MSIX packaged app.
    /// </summary>
    private static bool IsPackagedApp()
    {
        try
        {
            // GetCurrentPackageFullName returns 0 (ERROR_SUCCESS) for packaged apps
            int length = 0;
            int result = GetCurrentPackageFullName(ref length, null);
            return result != APPMODEL_ERROR_NO_PACKAGE;
        }
        catch
        {
            return false;
        }
    }

    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
}
