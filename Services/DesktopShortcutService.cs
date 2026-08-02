using System.Runtime.InteropServices;
using System.Text;

namespace SimpitLauncher.Services;

public static class DesktopShortcutService
{
    public static (string StartPath, string StopPath) CreateProfileShortcuts(string profileId, string profileName)
    {
        var exe = ResolveExePath();
        var workDir = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
        var startIcon = ResolveAssetIcon(workDir, "StartShortcut.ico");
        var stopIcon = ResolveAssetIcon(workDir, "StopShortcut.ico");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Directory.CreateDirectory(desktop);

        var safeName = SanitizeFileName(string.IsNullOrWhiteSpace(profileName) ? profileId : profileName);
        var startPath = Path.Combine(desktop, $"{safeName} Start.lnk");
        var stopPath = Path.Combine(desktop, $"{safeName} Stop.lnk");

        CreateShortcut(
            startPath,
            exe,
            $"--profile {profileId} --start --exit",
            workDir,
            startIcon,
            $"Start the {safeName} profile in Simpit Launcher");

        CreateShortcut(
            stopPath,
            exe,
            $"--profile {profileId} --stop --exit",
            workDir,
            stopIcon,
            $"Stop the {safeName} profile in Simpit Launcher");

        // Distinct AppUserModelIDs so these shortcuts don't override the main window taskbar icon.
        AppIdentity.SetShortcutAppUserModelId(startPath, AppIdentity.ShortcutId(profileId, "Start"));
        AppIdentity.SetShortcutAppUserModelId(stopPath, AppIdentity.ShortcutId(profileId, "Stop"));

        return (startPath, stopPath);
    }

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string arguments,
        string workingDirectory,
        string iconPath,
        string description)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell COM type not available.");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Failed to create WScript.Shell.");

        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.Arguments = arguments;
            shortcut.WorkingDirectory = workingDirectory;
            shortcut.WindowStyle = 1; // normal
            shortcut.Description = description;
            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                shortcut.IconLocation = $"{iconPath},0";
            }
            else
            {
                shortcut.IconLocation = $"{targetPath},0";
            }

            shortcut.Save();
            Marshal.FinalReleaseComObject(shortcut);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static string ResolveExePath()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            throw new InvalidOperationException("Could not resolve SimpitLauncher.exe path.");
        }

        return exe;
    }

    private static string ResolveAssetIcon(string workDir, string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(workDir, "Assets", fileName),
            Path.Combine(AppContext.BaseDirectory, "Assets", fileName),
            Path.Combine(workDir, fileName)
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? Path.Combine(workDir, "Assets", "AppIcon.ico");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Trim().Length);
        foreach (var ch in name.Trim())
        {
            sb.Append(invalid.Contains(ch) ? ' ' : ch);
        }

        var cleaned = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(cleaned) ? "Profile" : cleaned;
    }
}
