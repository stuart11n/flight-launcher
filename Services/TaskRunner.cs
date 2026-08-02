using System.Diagnostics;
using System.Text;
using FlightLauncher.Models;

namespace FlightLauncher.Services;

public sealed class TaskRunner
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    public async Task<string> RunStartAsync(IEnumerable<TaskEntry> tasks, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var log = new StringBuilder();
        foreach (var task in tasks.Where(t => t.Enabled))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var line = await StartOneAsync(task, ct).ConfigureAwait(false);
                Append(log, progress, line);
            }
            catch (Exception ex)
            {
                Append(log, progress, $"ERROR [{task.Name}] {ex.Message}");
            }
        }

        return log.ToString();
    }

    public async Task<string> RunStopAsync(IEnumerable<TaskEntry> tasks, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var log = new StringBuilder();
        foreach (var task in tasks.Where(t => t.Enabled))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var line = await StopOneAsync(task, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    line = $"SKIP [{task.Name}]";
                }

                Append(log, progress, line);
            }
            catch (Exception ex)
            {
                Append(log, progress, $"ERROR [{task.Name}] {ex.Message}");
            }
        }

        return log.ToString();
    }

    private static async Task<string> StartOneAsync(TaskEntry task, CancellationToken ct)
    {
        return task.Kind switch
        {
            TaskKind.Webhook => await StartWebhookAsync(task, ct).ConfigureAwait(false),
            TaskKind.Builtin => await RunBuiltinAsync(task, starting: true, ct).ConfigureAwait(false),
            _ => StartExecutable(task)
        };
    }

    private static async Task<string> StopOneAsync(TaskEntry task, CancellationToken ct)
    {
        return task.Kind switch
        {
            TaskKind.Webhook => await StopWebhookAsync(task, ct).ConfigureAwait(false),
            TaskKind.Builtin => await RunBuiltinAsync(task, starting: false, ct).ConfigureAwait(false),
            _ => StopExecutable(task)
        };
    }

    private static async Task<string> StartWebhookAsync(TaskEntry task, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.StartUrl))
        {
            return $"SKIP [{task.Name}] no start URL";
        }

        using var response = await Http.GetAsync(task.StartUrl, ct).ConfigureAwait(false);
        return $"WEBHOOK [{task.Name}] GET {task.StartUrl} -> {(int)response.StatusCode}";
    }

    private static async Task<string> StopWebhookAsync(TaskEntry task, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.StopUrl))
        {
            return $"SKIP [{task.Name}] no stop URL";
        }

        using var response = await Http.GetAsync(task.StopUrl, ct).ConfigureAwait(false);
        return $"WEBHOOK [{task.Name}] GET {task.StopUrl} -> {(int)response.StatusCode}";
    }

    private static async Task<string> RunBuiltinAsync(TaskEntry task, bool starting, CancellationToken ct)
    {
        switch (task.BuiltinAction)
        {
            case BuiltinAction.DisableFirewall:
                {
                    // Session START disables firewall; session STOP re-enables it (matches flight.bat / off.bat).
                    if (starting)
                    {
                        var exit = RunElevatedNetsh("advfirewall set allprofiles state off");
                        return $"START ACTION [{task.Name}] firewall OFF (netsh allprofiles state off, exit {exit})";
                    }
                    else
                    {
                        var exit = RunElevatedNetsh("advfirewall set allprofiles state on");
                        return $"STOP ACTION [{task.Name}] firewall ON (netsh allprofiles state on, exit {exit})";
                    }
                }
            case BuiltinAction.DisableRealtimeScanning:
                {
                    // Session START disables Defender realtime; session STOP re-enables it (matches flight.bat / off.bat).
                    if (starting)
                    {
                        RunElevatedDefenderRealtime(disabled: true);
                        return $"START ACTION [{task.Name}] Defender realtime OFF (DisableRealtimeMonitoring=true)";
                    }
                    else
                    {
                        RunElevatedDefenderRealtime(disabled: false);
                        return $"STOP ACTION [{task.Name}] Defender realtime ON (DisableRealtimeMonitoring=false)";
                    }
                }
            case BuiltinAction.MaxCpuPerformance:
                {
                    // Same command both ways: powercfg /s <guid>
                    var powercfg = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
                    if (starting)
                    {
                        RunProcess(powercfg, $"/s {HighPerfGuid}");
                        return $"START ACTION [{task.Name}] powercfg /s High performance ({HighPerfGuid})";
                    }
                    else
                    {
                        RunProcess(powercfg, $"/s {BalancedGuid}");
                        return $"STOP ACTION [{task.Name}] powercfg /s Balanced ({BalancedGuid})";
                    }
                }
            case BuiltinAction.MaxGpuPerformance:
                {
                    // Same command both ways: nvidia-smi -pl <watts>
                    var watts = starting
                        ? (task.GpuPowerLimitWatts <= 0 ? 352 : task.GpuPowerLimitWatts)
                        : (task.GpuStopPowerLimitWatts <= 0 ? 200 : task.GpuStopPowerLimitWatts);
                    var exit = RunElevatedProcess("nvidia-smi", $"-pl {watts}");
                    if (exit is null)
                    {
                        throw new InvalidOperationException("UAC cancelled or failed to start nvidia-smi.");
                    }

                    if (exit != 0)
                    {
                        throw new InvalidOperationException($"nvidia-smi exited with code {exit}.");
                    }

                    return starting
                        ? $"START ACTION [{task.Name}] nvidia-smi -pl {watts} (exit {exit})"
                        : $"STOP ACTION [{task.Name}] nvidia-smi -pl {watts} (exit {exit})";
                }
            default:
                await Task.CompletedTask.ConfigureAwait(false);
                return $"SKIP [{task.Name}] unknown builtin";
        }
    }

    private static string StartExecutable(TaskEntry task)
    {
        if (string.IsNullOrWhiteSpace(task.Path))
        {
            return $"SKIP [{task.Name}] no start path";
        }

        if (task.KillBeforeLaunch)
        {
            var images = ResolveImageNames(task.KillImageName, string.Empty, task.Path);
            if (!string.IsNullOrWhiteSpace(images))
            {
                KillImages(images, task.KillBeforeLaunchForce);
            }
        }

        LaunchPath(task.Path, task.Arguments, task.RunAsAdministrator);
        return $"START [{task.Name}] {task.Path} {task.Arguments}".TrimEnd();
    }

    private static string StopExecutable(TaskEntry task)
    {
        switch (task.StopMode)
        {
            case StopMode.None:
                return $"SKIP [{task.Name}] stop mode none";
            case StopMode.Kill:
            case StopMode.ForceKill:
                {
                    var images = ResolveImageNames(task.StopImageName, task.KillImageName, task.Path);
                    if (string.IsNullOrWhiteSpace(images))
                    {
                        return $"SKIP [{task.Name}] no stop image name";
                    }

                    var force = task.StopMode == StopMode.ForceKill;
                    var result = KillImages(images, force);
                    return $"STOP [{task.Name}] {(force ? "force " : "")}kill {images} -> {result}";
                }
            case StopMode.CommandLine:
                {
                    if (string.IsNullOrWhiteSpace(task.StopCommand))
                    {
                        return $"SKIP [{task.Name}] empty stop command";
                    }

                    RunCommandLine(task.StopCommand, task.RunAsAdministrator);
                    return $"STOP [{task.Name}] cmd: {task.StopCommand}";
                }
            default:
                return $"SKIP [{task.Name}] unknown stop mode";
        }
    }

    private static string KillImages(string imageList, bool force)
    {
        var parts = new List<string>();
        foreach (var image in SplitImages(imageList))
        {
            parts.Add(KillOneImage(image, force));
        }

        return parts.Count == 0 ? "nothing to kill" : string.Join("; ", parts);
    }

    private static string KillOneImage(string image, bool force)
    {
        var taskkill = Path.Combine(Environment.SystemDirectory, "taskkill.exe");
        // /T kills child processes; /F is force. Quote image names for wildcards like SPAD.neXt*.
        var args = force ? $"/F /T /IM \"{image}\"" : $"/T /IM \"{image}\"";

        // Prefer a direct (non-shell) launch; do not redirect stdio (avoids WaitForExit deadlocks).
        var exit = StartProcess(taskkill, args, runAsAdmin: false, redirect: false);
        if (exit is 0 or 128)
        {
            return exit == 0 ? $"ok {image}" : $"not running {image}";
        }

        // Soft kill often fails for apps that ignore WM_CLOSE — escalate to force once.
        if (!force)
        {
            var forced = StartProcess(taskkill, $"/F /T /IM \"{image}\"", runAsAdmin: false, redirect: false);
            if (forced is 0 or 128)
            {
                return forced == 0 ? $"ok forced {image}" : $"not running {image}";
            }

            exit = forced ?? exit;
        }

        // Access denied / elevated target processes: retry elevated force-kill.
        var elevated = StartProcess(taskkill, $"/F /T /IM \"{image}\"", runAsAdmin: true, redirect: false);
        if (elevated is null)
        {
            return $"failed {image} (exit {exit}; UAC cancelled on retry)";
        }

        return elevated is 0 or 128
            ? (elevated == 0 ? $"ok elevated {image}" : $"not running {image}")
            : $"failed {image} (exit {elevated})";
    }

    private static IEnumerable<string> SplitImages(string imageList) =>
        imageList.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ResolveImageNames(string? stopImageName, string? killImageName, string? path)
    {
        var explicitNames = FirstNonEmpty(stopImageName ?? string.Empty, killImageName ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(explicitNames))
        {
            return explicitNames;
        }

        if (string.IsNullOrWhiteSpace(path) || IsUri(path))
        {
            return string.Empty;
        }

        try
        {
            var fileName = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(fileName) ? string.Empty : fileName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void LaunchPath(string path, string arguments, bool runAsAdmin)
    {
        if (IsUri(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            return;
        }

        var workingDirectory = TryGetDirectory(path);
        var psi = new ProcessStartInfo
        {
            FileName = path,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        if (runAsAdmin)
        {
            psi.Verb = "runas";
        }

        Process.Start(psi);
    }

    private static void RunCommandLine(string command, bool runAsAdmin)
    {
        // Prefer launching a quoted path directly so cwd can be derived.
        var trimmed = command.Trim();
        if (trimmed.StartsWith("call ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[5..].Trim();
        }

        if (LooksLikePathCommand(trimmed, out var file, out var args))
        {
            LaunchPath(file, args, runAsAdmin);
            return;
        }

        RunProcess("cmd.exe", $"/c {command}", runAsAdmin: runAsAdmin);
    }

    private static bool LooksLikePathCommand(string command, out string file, out string args)
    {
        file = string.Empty;
        args = string.Empty;

        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end <= 1)
            {
                return false;
            }

            file = command[1..end];
            args = command[(end + 1)..].Trim();
            return File.Exists(file) || IsUri(file);
        }

        var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        file = parts[0];
        args = parts.Length > 1 ? parts[1] : string.Empty;
        return file.Contains('\\') || file.Contains('/') || file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
    }

    private static void RunProcess(string fileName, string arguments, bool runAsAdmin = false, bool ignoreExitCode = false)
    {
        var exit = StartProcess(fileName, arguments, runAsAdmin, redirect: !runAsAdmin);
        if (!ignoreExitCode && exit is not null and not 0 and not 128)
        {
            // taskkill returns 128 when process not found; other tools vary.
        }
    }

    private static int RunElevatedNetsh(string arguments)
    {
        var netsh = Path.Combine(Environment.SystemDirectory, "netsh.exe");
        var exit = RunElevatedProcess(netsh, arguments);
        if (exit is null)
        {
            throw new InvalidOperationException("UAC cancelled or failed to start netsh.");
        }

        if (exit != 0)
        {
            throw new InvalidOperationException($"netsh exited with code {exit}.");
        }

        return exit.Value;
    }

    private static void RunElevatedDefenderRealtime(bool disabled)
    {
        var ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        var flag = disabled ? "$true" : "$false";
        // EncodedCommand avoids ShellExecute quoting issues; verify the preference actually stuck.
        var script =
            $"Set-MpPreference -DisableRealtimeMonitoring:{flag}; " +
            "if (-not $?) { exit 1 }; " +
            "$pref = Get-MpPreference; " +
            $"if ($pref.DisableRealtimeMonitoring -ne {flag}) {{ " +
            "Write-Error 'Preference did not change (Tamper Protection may be blocking).'; exit 2 }";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var exit = RunElevatedProcess(ps, $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}");
        if (exit is null)
        {
            throw new InvalidOperationException("UAC cancelled or failed to start PowerShell.");
        }

        if (exit != 0)
        {
            throw new InvalidOperationException(
                exit == 2
                    ? "Defender preference did not change (check Tamper Protection)."
                    : $"PowerShell exited with code {exit}.");
        }
    }

    /// <returns>Exit code, or null if the process could not be started / UAC cancelled.</returns>
    private static int? RunElevatedProcess(string fileName, string arguments) =>
        StartProcess(fileName, arguments, runAsAdmin: true, redirect: false);

    private static int? StartProcess(string fileName, string arguments, bool runAsAdmin, bool redirect)
    {
        // Never redirect when UseShellExecute is true (invalid). Avoid redirect+WaitForExit
        // deadlocks by defaulting kill/elevated tools to no redirect.
        var useShell = runAsAdmin;
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = useShell,
            CreateNoWindow = !useShell,
            RedirectStandardOutput = !useShell && redirect,
            RedirectStandardError = !useShell && redirect
        };

        if (runAsAdmin)
        {
            psi.Verb = "runas";
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            if (psi.RedirectStandardOutput)
            {
                _ = process.StandardOutput.ReadToEnd();
            }

            if (psi.RedirectStandardError)
            {
                _ = process.StandardError.ReadToEnd();
            }

            if (!process.WaitForExit(60_000))
            {
                throw new TimeoutException($"Timed out waiting for {fileName}.");
            }

            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED - user dismissed UAC.
            return null;
        }
    }

    private static bool IsUri(string path) =>
        path.Contains("://", StringComparison.Ordinal);

    private static string? TryGetDirectory(string path)
    {
        try
        {
            if (IsUri(path))
            {
                return null;
            }

            var full = Path.GetFullPath(path);
            return Path.GetDirectoryName(full);
        }
        catch
        {
            return null;
        }
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static void Append(StringBuilder log, IProgress<string>? progress, string line)
    {
        log.AppendLine(line);
        progress?.Report(line);
    }
}
