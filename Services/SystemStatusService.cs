using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace FlightLauncher.Services;

public sealed record SystemStatusSnapshot(
    string GpuPowerLimit,
    string CpuMode,
    string Firewall,
    string ThreatProtection);

public static class SystemStatusService
{
    private static readonly Regex GuidRegex = new(
        @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
        RegexOptions.Compiled);

    private const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    public static Task<SystemStatusSnapshot> QueryAsync(CancellationToken ct = default) =>
        Task.Run(() => Query(ct), ct);

    public static SystemStatusSnapshot Query(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new SystemStatusSnapshot(
            QueryGpuPowerLimit(),
            QueryCpuMode(),
            QueryFirewall(),
            QueryThreatProtection());
    }

    private static string QueryGpuPowerLimit()
    {
        try
        {
            var (exit, stdout, _) = RunCapture(
                "nvidia-smi",
                "--query-gpu=power.limit --format=csv,noheader,nounits",
                TimeSpan.FromSeconds(8));

            if (exit != 0)
            {
                return "n/a";
            }

            var line = stdout
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .FirstOrDefault(s => s.Length > 0);

            if (string.IsNullOrWhiteSpace(line) ||
                line.Contains("N/A", StringComparison.OrdinalIgnoreCase))
            {
                return "n/a";
            }

            if (double.TryParse(line, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var watts))
            {
                return $"{watts:0.#} W";
            }

            return $"{line} W";
        }
        catch
        {
            return "n/a";
        }
    }

    private static string QueryCpuMode()
    {
        try
        {
            var powercfg = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
            var (exit, stdout, _) = RunCapture(powercfg, "/getactivescheme", TimeSpan.FromSeconds(5));
            if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                return "n/a";
            }

            var guidMatch = GuidRegex.Match(stdout);
            var guid = guidMatch.Success ? guidMatch.Groups[1].Value : string.Empty;

            // Typical: Power Scheme GUID: ...  (High performance)
            var nameMatch = Regex.Match(stdout, @"\(([^)]+)\)\s*$", RegexOptions.Multiline);
            var name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : string.Empty;

            if (string.Equals(guid, HighPerfGuid, StringComparison.OrdinalIgnoreCase))
            {
                return "High performance";
            }

            if (string.Equals(guid, BalancedGuid, StringComparison.OrdinalIgnoreCase))
            {
                return "Balanced";
            }

            return string.IsNullOrWhiteSpace(name) ? (string.IsNullOrWhiteSpace(guid) ? "n/a" : guid) : name;
        }
        catch
        {
            return "n/a";
        }
    }

    private static string QueryFirewall()
    {
        try
        {
            var netsh = Path.Combine(Environment.SystemDirectory, "netsh.exe");
            var (exit, stdout, _) = RunCapture(
                netsh,
                "advfirewall show allprofiles state",
                TimeSpan.FromSeconds(8));

            if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                return "n/a";
            }

            // Lines like: State                                 ON
            var states = new List<string>();
            foreach (var raw in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (!line.StartsWith("State", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    states.Add(parts[^1].ToUpperInvariant());
                }
            }

            if (states.Count == 0)
            {
                return "n/a";
            }

            var on = states.Count(s => s is "ON");
            var off = states.Count(s => s is "OFF");
            if (on == states.Count)
            {
                return "On";
            }

            if (off == states.Count)
            {
                return "Off";
            }

            return $"Mixed ({on} on / {off} off)";
        }
        catch
        {
            return "n/a";
        }
    }

    private static string QueryThreatProtection()
    {
        try
        {
            // Prefer computer status (actual protection state) over preference alone.
            var (exit, stdout, _) = RunCapture(
                "powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"(Get-MpComputerStatus).RealTimeProtectionEnabled\"",
                TimeSpan.FromSeconds(12));

            if (exit == 0)
            {
                var token = stdout.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .LastOrDefault();

                if (bool.TryParse(token, out var enabled))
                {
                    return enabled ? "On" : "Off";
                }

                if (string.Equals(token, "True", StringComparison.OrdinalIgnoreCase))
                {
                    return "On";
                }

                if (string.Equals(token, "False", StringComparison.OrdinalIgnoreCase))
                {
                    return "Off";
                }
            }

            // Fallback: preference DisableRealtimeMonitoring (true => protection off)
            var (exit2, stdout2, _) = RunCapture(
                "powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"(Get-MpPreference).DisableRealtimeMonitoring\"",
                TimeSpan.FromSeconds(12));

            if (exit2 != 0)
            {
                return "n/a";
            }

            var pref = stdout2.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .LastOrDefault();

            if (bool.TryParse(pref, out var disabled))
            {
                return disabled ? "Off" : "On";
            }

            return "n/a";
        }
        catch
        {
            return "n/a";
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunCapture(
        string fileName,
        string arguments,
        TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw new TimeoutException($"{fileName} timed out");
        }

        return (process.ExitCode, stdout, stderr);
    }
}
