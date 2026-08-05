using Microsoft.Win32;

namespace SimpitLauncher.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SimpitLauncher";
    private const string LegacyValueName = "FlightLauncher";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return HasValue(key, ValueName) || HasValue(key, LegacyValueName);
    }

    public static void SetEnabled(bool enabled, bool startMinimized = false)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        // Always clear the old FlightLauncher startup entry on change.
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (string.IsNullOrWhiteSpace(exe))
        {
            throw new InvalidOperationException("Could not resolve application path for startup registration.");
        }

        var command = startMinimized ? $"\"{exe}\" --minimized" : $"\"{exe}\"";
        key.SetValue(ValueName, command);
    }

    private static bool HasValue(RegistryKey? key, string name) =>
        !string.IsNullOrWhiteSpace(key?.GetValue(name) as string);
}
