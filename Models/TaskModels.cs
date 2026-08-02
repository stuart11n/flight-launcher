using System.Text.Json.Serialization;

namespace FlightLauncher.Models;

public enum TaskKind
{
    Executable,
    Webhook,
    Builtin
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
    MaxGpuPerformance
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

    // Builtin
    public BuiltinAction BuiltinAction { get; set; } = BuiltinAction.None;
    public int GpuPowerLimitWatts { get; set; } = 352;

    [JsonIgnore]
    public string TypeLabel => Kind switch
    {
        TaskKind.Webhook => "Webhook",
        TaskKind.Builtin => "System",
        _ => "Executable"
    };

    [JsonIgnore]
    public string Summary
    {
        get
        {
            return Kind switch
            {
                TaskKind.Webhook => string.IsNullOrWhiteSpace(StartUrl)
                    ? (string.IsNullOrWhiteSpace(StopUrl) ? "(no URL)" : $"Stop: {StopUrl}")
                    : StartUrl,
                TaskKind.Builtin => BuiltinAction switch
                {
                    BuiltinAction.DisableFirewall => "Firewall off / on",
                    BuiltinAction.DisableRealtimeScanning => "Defender realtime off / on",
                    BuiltinAction.MaxCpuPerformance => "Power plan High / Balanced",
                    BuiltinAction.MaxGpuPerformance => $"nvidia-smi -pl {GpuPowerLimitWatts}",
                    _ => "System"
                },
                _ => string.IsNullOrWhiteSpace(Path)
                    ? (StopMode == StopMode.CommandLine
                        ? $"Stop cmd: {StopCommand}"
                        : $"Stop: {StopMode}")
                    : string.IsNullOrWhiteSpace(Arguments) ? Path : $"{Path} {Arguments}"
            };
        }
    }
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
