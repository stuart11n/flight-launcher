using System.Text.Json.Serialization;

namespace SimpitLauncher.Models;

public enum TaskKind
{
    Executable,
    Webhook,
    Builtin,
    Shelly,
    ComCommand
}

public enum StopMode
{
    None,
    Kill,
    ForceKill,
    CommandLine
}

public enum BuiltinAction
{
    None,
    DisableFirewall,
    DisableRealtimeScanning,
    MaxCpuPerformance,
    MaxGpuPerformance,
    DisableUsbPowerSaving
}

public sealed class TaskEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public TaskKind Kind { get; set; } = TaskKind.Executable;

    // Executable
    public string Path { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public bool RunAsAdministrator { get; set; }
    public bool KillBeforeLaunch { get; set; }
    public bool KillBeforeLaunchForce { get; set; }
    public string KillImageName { get; set; } = string.Empty;
    public StopMode StopMode { get; set; } = StopMode.None;
    public string StopImageName { get; set; } = string.Empty;
    public string StopCommand { get; set; } = string.Empty;

    // Webhook
    public string StartUrl { get; set; } = string.Empty;
    public string StopUrl { get; set; } = string.Empty;

    // Shelly (IP-only relay webhook)
    public string IpAddress { get; set; } = string.Empty;

    // COM command (serial port write)
    public string ComPort { get; set; } = string.Empty;
    public string ComStartText { get; set; } = string.Empty;
    public string ComStopText { get; set; } = string.Empty;
    public int ComBaudRate { get; set; } = 0;

    // Builtin
    public BuiltinAction BuiltinAction { get; set; } = BuiltinAction.None;
    public int GpuPowerLimitWatts { get; set; } = 352;
    public int GpuStopPowerLimitWatts { get; set; } = 200;
    /// <summary>When true, STOP skips this system option (START still runs).</summary>
    public bool DisableStopAction { get; set; }

    /// <summary>
    /// Seconds to wait before running this entry. When &gt; 0, START/STOP schedules the
    /// action in the background and continues to the next entry immediately.
    /// </summary>
    public int DelaySeconds { get; set; }

    [JsonIgnore]
    public string TypeLabel => Kind switch
    {
        TaskKind.Webhook => "Webhook",
        TaskKind.Builtin => "System",
        TaskKind.Shelly => "Shelly",
        TaskKind.ComCommand => "COM",
        _ => "Executable"
    };

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var body = Kind switch
            {
                TaskKind.Webhook => string.IsNullOrWhiteSpace(StartUrl)
                    ? (string.IsNullOrWhiteSpace(StopUrl) ? "(no URL)" : $"Stop: {StopUrl}")
                    : StartUrl,
                TaskKind.Shelly => ShellySummary(),
                TaskKind.ComCommand => ComSummary(),
                TaskKind.Builtin => BuiltinSummary(),
                _ => string.IsNullOrWhiteSpace(Path)
                    ? (StopMode == StopMode.CommandLine
                        ? $"Stop cmd: {StopCommand}"
                        : $"Stop: {StopMode}")
                    : string.IsNullOrWhiteSpace(Arguments) ? Path : $"{Path} {Arguments}"
            };

            return DelaySeconds > 0 ? $"Delay {DelaySeconds}s · {body}" : body;
        }
    }

    private string ShellySummary()
    {
        var ip = ResolveShellyIp();
        return string.IsNullOrWhiteSpace(ip) ? "(no IP)" : $"http://{ip}/relay/0?turn=on|off";
    }

    /// <summary>
    /// Shelly IP from <see cref="IpAddress"/>, or extracted from legacy Start/Stop webhook URLs.
    /// </summary>
    public string ResolveShellyIp()
    {
        var direct = (IpAddress ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(direct) && System.Net.IPAddress.TryParse(direct, out _))
        {
            return direct;
        }

        return TryExtractIpFromUrl(StartUrl) ?? TryExtractIpFromUrl(StopUrl) ?? string.Empty;
    }

    /// <summary>
    /// Copies a valid IP from legacy Start/Stop URLs into <see cref="IpAddress"/> when missing.
    /// </summary>
    public bool TryMigrateShellyIpFromUrls()
    {
        if (Kind != TaskKind.Shelly || !string.IsNullOrWhiteSpace(IpAddress))
        {
            return false;
        }

        var ip = ResolveShellyIp();
        if (string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        IpAddress = ip;
        return true;
    }

    private static string? TryExtractIpFromUrl(string? url)
    {
        var text = (url ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (!Uri.TryCreate(text.Contains("://", StringComparison.Ordinal) ? text : $"http://{text}",
                UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host;
        return System.Net.IPAddress.TryParse(host, out _) ? host : null;
    }

    private string ComSummary()
    {
        var port = string.IsNullOrWhiteSpace(ComPort) ? "(no COM)" : ComPort.Trim().ToUpperInvariant();
        var start = string.IsNullOrEmpty(ComStartText) ? "—" : QuotePreview(ComStartText);
        var stop = string.IsNullOrEmpty(ComStopText) ? "—" : QuotePreview(ComStopText);
        return ComBaudRate > 0
            ? $"{port}@{ComBaudRate}: start {start} / stop {stop}"
            : $"{port}: start {start} / stop {stop}";
    }

    private static string QuotePreview(string text)
    {
        var preview = text.Length <= 24 ? text : text[..21] + "…";
        return $"\"{preview.Replace("\r", "\\r").Replace("\n", "\\n")}\"";
    }

    private string BuiltinSummary()
    {
        var startOnly = DisableStopAction;
        return BuiltinAction switch
        {
            BuiltinAction.DisableFirewall => startOnly ? "Firewall off (no stop)" : "Firewall off / on",
            BuiltinAction.DisableRealtimeScanning => startOnly
                ? "Realtime threat scanning off (no stop)"
                : "Realtime threat scanning off / on",
            BuiltinAction.MaxCpuPerformance => startOnly
                ? "Power plan High (no stop)"
                : "Power plan High / Balanced",
            BuiltinAction.MaxGpuPerformance => startOnly
                ? $"nvidia-smi -pl {GpuPowerLimitWatts} (no stop)"
                : $"nvidia-smi -pl {GpuPowerLimitWatts} / stop {GpuStopPowerLimitWatts}",
            BuiltinAction.DisableUsbPowerSaving => startOnly
                ? "USB power saving off (no stop)"
                : "USB power saving off / on",
            _ => "System"
        };
    }

    public TaskEntry Clone() => new()
    {
        Id = Id,
        Name = Name,
        Enabled = Enabled,
        Kind = Kind,
        Path = Path,
        Arguments = Arguments,
        RunAsAdministrator = RunAsAdministrator,
        KillBeforeLaunch = KillBeforeLaunch,
        KillBeforeLaunchForce = KillBeforeLaunchForce,
        KillImageName = KillImageName,
        StopMode = StopMode,
        StopImageName = StopImageName,
        StopCommand = StopCommand,
        StartUrl = StartUrl,
        StopUrl = StopUrl,
        IpAddress = IpAddress,
        ComPort = ComPort,
        ComStartText = ComStartText,
        ComStopText = ComStopText,
        ComBaudRate = ComBaudRate,
        BuiltinAction = BuiltinAction,
        GpuPowerLimitWatts = GpuPowerLimitWatts,
        GpuStopPowerLimitWatts = GpuStopPowerLimitWatts,
        DisableStopAction = DisableStopAction,
        DelaySeconds = DelaySeconds
    };
}

public sealed class ModeConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<TaskEntry> Tasks { get; set; } = [];
}

public sealed class AppSettings
{
    public const string FlightModeId = "flight";
    public const string RacingModeId = "racing";

    public bool StartOnLogin { get; set; }
    public bool StartMinimized { get; set; }
    public string ActiveModeId { get; set; } = FlightModeId;
    public List<ModeConfig> Modes { get; set; } = [];

    /// <summary>Legacy single-list settings; used only for migration.</summary>
    public List<TaskEntry>? Tasks { get; set; }

    public ModeConfig GetActiveMode()
    {
        EnsureModes();
        return Modes.FirstOrDefault(m => string.Equals(m.Id, ActiveModeId, StringComparison.OrdinalIgnoreCase))
            ?? Modes[0];
    }

    public void EnsureModes()
    {
        if (Modes.Count == 0)
        {
            Modes =
            [
                new ModeConfig { Id = FlightModeId, Name = "Flight", Tasks = [] },
                new ModeConfig { Id = RacingModeId, Name = "Racing", Tasks = [] }
            ];
        }
        else
        {
            if (!Modes.Any(m => string.Equals(m.Id, FlightModeId, StringComparison.OrdinalIgnoreCase)))
            {
                Modes.Insert(0, new ModeConfig { Id = FlightModeId, Name = "Flight", Tasks = [] });
            }

            if (!Modes.Any(m => string.Equals(m.Id, RacingModeId, StringComparison.OrdinalIgnoreCase)))
            {
                Modes.Add(new ModeConfig { Id = RacingModeId, Name = "Racing", Tasks = [] });
            }
        }

        if (string.IsNullOrWhiteSpace(ActiveModeId) ||
            !Modes.Any(m => string.Equals(m.Id, ActiveModeId, StringComparison.OrdinalIgnoreCase)))
        {
            ActiveModeId = FlightModeId;
        }
    }
}
