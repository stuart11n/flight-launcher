using FlightLauncher.Models;

namespace FlightLauncher.Services;

public static class DefaultTasks
{
    private const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    private static readonly string PimaxStopImages = string.Join(", ",
    [
        "PimaxClient.exe",
        "DeviceSetting.exe",
        "pi_server.exe",
        "PiPlatformService_64.exe",
        "PiPlayService.exe",
        "PiService.exe",
        "NoloServer.exe",
        "platform_runtime_VR4PIMAXP3B_service.exe"
    ]);

    public static AppSettings CreateSettings() => new()
    {
        ActiveModeId = AppSettings.FlightModeId,
        Modes =
        [
            new ModeConfig
            {
                Id = AppSettings.FlightModeId,
                Name = "Flight",
                Tasks = Create()
            },
            new ModeConfig
            {
                Id = AppSettings.RacingModeId,
                Name = "Racing",
                Tasks = []
            }
        ]
    };

    public static List<TaskEntry> Create()
    {
        // Ordered primarily for Stop (off.bat); start-only rows run in place on Start.
        return
        [
            Exe("Display switch clone", path: string.Empty, stopMode: StopMode.CommandLine,
                stopCommand: "DisplaySwitch.exe /clone"),

            Builtin("Max CPU performance", BuiltinAction.MaxCpuPerformance),

            Webhook("Relay 183",
                start: "http://192.168.74.183/relay/0?turn=on",
                stop: "http://192.168.74.183/relay/0?turn=off"),
            Webhook("Relay 184",
                start: string.Empty,
                stop: "http://192.168.74.184/relay/0?turn=off"),
            Webhook("Relay 185",
                start: "http://192.168.74.185/relay/0?turn=on",
                stop: "http://192.168.74.185/relay/0?turn=off"),
            Webhook("Relay 76",
                start: "http://192.168.74.76/relay/0?turn=on",
                stop: "http://192.168.74.76/relay/0?turn=off"),

            Exe("Steam MSFS",
                path: "steam://rungameid/2537590",
                stopMode: StopMode.None),

            Exe("Pimax Client",
                path: @"C:\Program Files\Pimax\PimaxClient\pimaxui\PimaxClient.exe",
                stopMode: StopMode.ForceKill,
                stopImageName: PimaxStopImages),

            Exe("SayIntentions AI",
                path: @"C:\Users\stuar\AppData\Roaming\SayIntentionsAI\SayIntentionsAI\SayIntentionsAI.exe",
                stopMode: StopMode.Kill,
                killImageName: "SayIntentionsAI.exe",
                stopImageName: "SayIntentionsAI.exe"),

            Exe("TV on/off",
                path: @"C:\Program Files (x86)\SimHub\ShellMacros\tv-on.bat",
                stopMode: StopMode.CommandLine,
                stopCommand: @"call ""C:\Program Files (x86)\SimHub\ShellMacros\tv-off.bat"""),

            Builtin("Disable Realtime Threat Scanning", BuiltinAction.DisableRealtimeScanning),
            Builtin("Disable firewall", BuiltinAction.DisableFirewall),
            Builtin("Max GPU performance", BuiltinAction.MaxGpuPerformance, gpuWatts: 352),

            Exe("PadForge",
                path: @"C:\Users\stuar\Documents\Stuart\PadForge\PadForge.exe",
                killBeforeLaunch: true,
                killBeforeLaunchForce: false,
                killImageName: "PadForge.exe",
                stopMode: StopMode.Kill,
                stopImageName: "PadForge.exe"),

            Exe("SimHub",
                path: @"C:\Program Files (x86)\SimHub\SimHubWPF.exe",
                killBeforeLaunch: true,
                killBeforeLaunchForce: true,
                killImageName: "SimHubWPF.exe*",
                stopMode: StopMode.CommandLine,
                stopCommand: @"""C:\Program Files (x86)\SimHub\SimHubWPF.exe"" -exit"),

            Exe("SPAD.neXt",
                path: @"C:\Program Files\SPAD.neXt\SPAD.neXt.exe",
                killBeforeLaunch: true,
                killBeforeLaunchForce: true,
                killImageName: "SPAD.neXt.exe*",
                stopMode: StopMode.Kill,
                stopImageName: "SPAD.neXt*"),

            Exe("FFBeast update profiles",
                path: @"C:\Program Files\ffbeast-flight-controls-ui\effect_profiles\update.bat",
                stopMode: StopMode.None),

            Exe("FFBeast Commander",
                path: @"C:\Program Files\ffbeast-flight-controls-ui\ffbeast-commander-RC.25.1.4.exe",
                killBeforeLaunch: true,
                killBeforeLaunchForce: true,
                killImageName: "ffbeast-commander*",
                stopMode: StopMode.Kill,
                stopImageName: "ffbeast-commander*"),

            Exe("Fanatec",
                path: string.Empty,
                stopMode: StopMode.ForceKill,
                stopImageName: "fanatec.exe"),

            Exe("Fly Radio",
                path: string.Empty,
                stopMode: StopMode.Kill,
                stopImageName: "FlyRadioWPF.exe*"),

            Exe("Stop PiServiceLauncher",
                path: string.Empty,
                stopMode: StopMode.CommandLine,
                stopCommand: "net stop PiServiceLauncher",
                runAsAdmin: true),

            Exe("Stop TobiiGeneric",
                path: string.Empty,
                stopMode: StopMode.CommandLine,
                stopCommand: "net stop TobiiGeneric",
                runAsAdmin: true),

            Exe("Backup Stuart docs",
                path: string.Empty,
                stopMode: StopMode.CommandLine,
                stopCommand: @"cmd /c copy /Y ""C:\Users\stuar\Documents\Stuart\*"" ""C:\Users\stuar\Sync\Backup\Stuart"""),

            Exe("Backup FFBeast effect profiles",
                path: string.Empty,
                stopMode: StopMode.CommandLine,
                stopCommand: @"cmd /c xcopy /yse ""C:\Program Files\ffbeast-flight-controls-ui\effect_profiles\*"" ""C:\Users\stuar\Sync\Backup\Stuart\effect_profiles"""),
        ];
    }

    private static TaskEntry Exe(
        string name,
        string path,
        string arguments = "",
        bool runAsAdmin = false,
        bool killBeforeLaunch = false,
        bool killBeforeLaunchForce = false,
        string killImageName = "",
        StopMode stopMode = StopMode.None,
        string stopImageName = "",
        string stopCommand = "") => new()
    {
        Name = name,
        Kind = TaskKind.Executable,
        Path = path,
        Arguments = arguments,
        RunAsAdministrator = runAsAdmin,
        KillBeforeLaunch = killBeforeLaunch,
        KillBeforeLaunchForce = killBeforeLaunchForce,
        KillImageName = killImageName,
        StopMode = stopMode,
        StopImageName = stopImageName,
        StopCommand = stopCommand
    };

    private static TaskEntry Webhook(string name, string start, string stop) => new()
    {
        Name = name,
        Kind = TaskKind.Webhook,
        StartUrl = start,
        StopUrl = stop
    };

    private static TaskEntry Builtin(string name, BuiltinAction action, int gpuWatts = 352, int gpuStopWatts = 200) => new()
    {
        Name = name,
        Kind = TaskKind.Builtin,
        BuiltinAction = action,
        GpuPowerLimitWatts = gpuWatts,
        GpuStopPowerLimitWatts = gpuStopWatts,
        // Keep GUIDs documented for runners that shell out via BuiltinAction.
        Arguments = action == BuiltinAction.MaxCpuPerformance
            ? $"{HighPerfGuid}|{BalancedGuid}"
            : string.Empty
    };
}
