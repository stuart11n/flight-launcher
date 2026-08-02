using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SimpitLauncher.Models;

namespace SimpitLauncher.Services;

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
                if (task.DelaySeconds > 0)
                {
                    ScheduleDeferred(task, starting: true, log, progress, ct);
                    continue;
                }

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
                if (task.DelaySeconds > 0)
                {
                    ScheduleDeferred(task, starting: false, log, progress, ct);
                    continue;
                }

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

    public async Task<string> RunSingleStartAsync(TaskEntry task, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try
        {
            if (task.DelaySeconds > 0)
            {
                var log = new StringBuilder();
                ScheduleDeferred(task, starting: true, log, progress, ct);
                return log.ToString().TrimEnd();
            }

            var line = await StartOneAsync(task, ct).ConfigureAwait(false);
            progress?.Report(line);
            return line;
        }
        catch (Exception ex)
        {
            var line = $"ERROR [{task.Name}] {ex.Message}";
            progress?.Report(line);
            return line;
        }
    }

    public async Task<string> RunSingleStopAsync(TaskEntry task, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try
        {
            if (task.DelaySeconds > 0)
            {
                var log = new StringBuilder();
                ScheduleDeferred(task, starting: false, log, progress, ct);
                return log.ToString().TrimEnd();
            }

            var line = await StopOneAsync(task, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                line = $"SKIP [{task.Name}]";
            }

            progress?.Report(line);
            return line;
        }
        catch (Exception ex)
        {
            var line = $"ERROR [{task.Name}] {ex.Message}";
            progress?.Report(line);
            return line;
        }
    }

    private static void ScheduleDeferred(
        TaskEntry task,
        bool starting,
        StringBuilder log,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var seconds = Math.Max(0, task.DelaySeconds);
        var snapshot = task.Clone();
        var action = starting ? "START" : "STOP";
        Append(log, progress, $"SCHEDULED {action} [{snapshot.Name}] in {seconds}s (background)");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);
                var line = starting
                    ? await StartOneAsync(snapshot, ct).ConfigureAwait(false)
                    : await StopOneAsync(snapshot, ct).ConfigureAwait(false);
                if (!starting && string.IsNullOrWhiteSpace(line))
                {
                    line = $"SKIP [{snapshot.Name}]";
                }

                progress?.Report(line);
            }
            catch (OperationCanceledException)
            {
                progress?.Report($"CANCELLED [{snapshot.Name}] delayed {action}");
            }
            catch (Exception ex)
            {
                progress?.Report($"ERROR [{snapshot.Name}] (delayed) {ex.Message}");
            }
        }, CancellationToken.None);
    }

    private static async Task<string> StartOneAsync(TaskEntry task, CancellationToken ct)
    {
        return task.Kind switch
        {
            TaskKind.Webhook => await StartWebhookAsync(task, ct).ConfigureAwait(false),
            TaskKind.Shelly => await StartShellyAsync(task, ct).ConfigureAwait(false),
            TaskKind.ComCommand => await RunComCommandAsync(task, starting: true, ct).ConfigureAwait(false),
            TaskKind.Builtin => await RunBuiltinAsync(task, starting: true, ct).ConfigureAwait(false),
            _ => StartExecutable(task)
        };
    }

    private static async Task<string> StopOneAsync(TaskEntry task, CancellationToken ct)
    {
        return task.Kind switch
        {
            TaskKind.Webhook => await StopWebhookAsync(task, ct).ConfigureAwait(false),
            TaskKind.Shelly => await StopShellyAsync(task, ct).ConfigureAwait(false),
            TaskKind.ComCommand => await RunComCommandAsync(task, starting: false, ct).ConfigureAwait(false),
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

    private static async Task<string> StartShellyAsync(TaskEntry task, CancellationToken ct)
    {
        var url = BuildShellyUrl(task.IpAddress, turnOn: true);
        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        return $"SHELLY [{task.Name}] GET {url} -> {(int)response.StatusCode}";
    }

    private static async Task<string> StopShellyAsync(TaskEntry task, CancellationToken ct)
    {
        var url = BuildShellyUrl(task.IpAddress, turnOn: false);
        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        return $"SHELLY [{task.Name}] GET {url} -> {(int)response.StatusCode}";
    }

    private static string BuildShellyUrl(string ipAddress, bool turnOn)
    {
        var ip = (ipAddress ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(ip) || !System.Net.IPAddress.TryParse(ip, out _))
        {
            throw new InvalidOperationException("Shelly task requires a valid IP address.");
        }

        return $"http://{ip}/relay/0?turn={(turnOn ? "on" : "off")}";
    }

    private static async Task<string> RunComCommandAsync(TaskEntry task, bool starting, CancellationToken ct)
    {
        var text = starting ? task.ComStartText : task.ComStopText;
        if (string.IsNullOrEmpty(text))
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return starting
                ? $"SKIP [{task.Name}] no start COM text"
                : $"SKIP [{task.Name}] no stop COM text";
        }

        var portName = NormalizeComPort(task.ComPort);
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new InvalidOperationException("COM command requires a port (e.g. COM13).");
        }

        // Match `echo TEXT > \\.\COMn`: device path write, CRLF terminator, no baud change unless set.
        var payload = DecodeEscapes(text);
        if (!payload.EndsWith('\n') && !payload.EndsWith('\r'))
        {
            payload += "\r\n";
        }

        var devicePath = $@"\\.\{portName}";
        var baud = task.ComBaudRate; // 0 = leave port settings unchanged (cmd echo behavior)
        var bytes = Encoding.ASCII.GetBytes(payload);

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            WriteComDevice(devicePath, bytes, baud);
        }, ct).ConfigureAwait(false);

        var preview = text.Length <= 40 ? text : text[..37] + "…";
        var baudLabel = baud > 0 ? $"@{baud}" : "";
        return $"COM [{task.Name}] {devicePath}{baudLabel} write \"{preview}\"";
    }

    /// <summary>
    /// Writes like <c>echo … &gt; \\.\COMn</c>: open the device path and write bytes.
    /// When baud is &lt;= 0, port settings are left alone (same as cmd redirection).
    /// </summary>
    private static void WriteComDevice(string devicePath, byte[] bytes, int baud)
    {
        if (baud > 0)
        {
            // Optional explicit baud: open briefly with SerialPort, avoid DTR/RTS toggles when possible.
            var portName = devicePath.StartsWith(@"\\.\", StringComparison.Ordinal)
                ? devicePath[4..]
                : devicePath;
            using var port = new System.IO.Ports.SerialPort(portName, baud)
            {
                Handshake = System.IO.Ports.Handshake.None,
                DtrEnable = false,
                RtsEnable = false,
                WriteTimeout = 3000
            };
            port.Open();
            port.Write(bytes, 0, bytes.Length);
            Thread.Sleep(50);
            return;
        }

        var handle = CreateFile(
            devicePath,
            GenericWrite,
            0,
            nint.Zero,
            OpenExisting,
            FileAttributeNormal,
            nint.Zero);

        if (handle == InvalidHandleValue || handle == nint.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Failed to open {devicePath} (Win32 {err}). Is the port in use?");
        }

        try
        {
            if (!WriteFile(handle, bytes, (uint)bytes.Length, out var written, nint.Zero) || written != (uint)bytes.Length)
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Failed to write {devicePath} (Win32 {err}).");
            }

            _ = FlushFileBuffers(handle);
            Thread.Sleep(50);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private static readonly nint InvalidHandleValue = new(-1);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        nint hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        nint lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushFileBuffers(nint hFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);

    private static string NormalizeComPort(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (text.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase))
        {
            text = text[4..];
        }

        if (int.TryParse(text, out var n) && n > 0)
        {
            return $"COM{n}";
        }

        if (text.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(text[3..], out n) && n > 0)
        {
            return $"COM{n}";
        }

        return text.ToUpperInvariant();
    }

    private static string DecodeEscapes(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                sb.Append(text[i + 1] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '\\' => '\\',
                    _ => text[i + 1]
                });
                i++;
                continue;
            }

            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    private static async Task<string> RunBuiltinAsync(TaskEntry task, bool starting, CancellationToken ct)
    {
        if (!starting && task.DisableStopAction)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return $"SKIP [{task.Name}] stop action disabled";
        }

        switch (task.BuiltinAction)
        {
            case BuiltinAction.DisableFirewall:
                {
                    // Session START disables firewall; session STOP re-enables it (matches flight.bat / off.bat).
                    if (starting)
                    {
                        WindowsProtectionService.SetFirewallEnabled(false);
                        return $"START ACTION [{task.Name}] firewall OFF (INetFwPolicy2)";
                    }
                    else
                    {
                        WindowsProtectionService.SetFirewallEnabled(true);
                        return $"STOP ACTION [{task.Name}] firewall ON (INetFwPolicy2)";
                    }
                }
            case BuiltinAction.DisableRealtimeScanning:
                {
                    // Session START disables Defender realtime; session STOP re-enables it (matches flight.bat / off.bat).
                    if (starting)
                    {
                        WindowsProtectionService.SetDefenderRealtimeDisabled(disabled: true);
                        return $"START ACTION [{task.Name}] Defender realtime OFF (MSFT_MpPreference)";
                    }
                    else
                    {
                        WindowsProtectionService.SetDefenderRealtimeDisabled(disabled: false);
                        return $"STOP ACTION [{task.Name}] Defender realtime ON (MSFT_MpPreference)";
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
            case BuiltinAction.DisableUsbPowerSaving:
                {
                    if (starting)
                    {
                        WindowsProtectionService.SetUsbPowerSavingEnabled(false);
                        return $"START ACTION [{task.Name}] USB power saving OFF (selective suspend + USB devices)";
                    }

                    WindowsProtectionService.SetUsbPowerSavingEnabled(true);
                    return $"STOP ACTION [{task.Name}] USB power saving ON (selective suspend + USB devices)";
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
