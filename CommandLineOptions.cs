using FlightLauncher.Models;

namespace FlightLauncher;

public sealed class CommandLineOptions
{
    public string? ProfileId { get; init; }
    public bool Start { get; init; }
    public bool Stop { get; init; }
    public bool ExitAfterAction { get; init; }
    public bool Minimized { get; init; }
    public bool ShowHelp { get; init; }

    public static CommandLineOptions Parse(string[] args)
    {
        string? profile = null;
        var start = false;
        var stop = false;
        var exitAfter = false;
        var minimized = false;
        var help = false;

        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i].Trim();
            if (raw.Length == 0)
            {
                continue;
            }

            var arg = raw.TrimStart('/', '-');
            var key = arg;
            string? value = null;

            var eq = arg.IndexOf('=');
            if (eq > 0)
            {
                key = arg[..eq];
                value = arg[(eq + 1)..];
            }

            switch (key.ToLowerInvariant())
            {
                case "help":
                case "h":
                case "?":
                    help = true;
                    break;
                case "start":
                    start = true;
                    break;
                case "stop":
                    stop = true;
                    break;
                case "exit":
                case "quit":
                    exitAfter = true;
                    break;
                case "minimized":
                case "minimize":
                case "tray":
                    minimized = true;
                    break;
                case "profile":
                case "mode":
                case "p":
                    value ??= TakeNext(args, ref i);
                    profile = NormalizeProfile(value);
                    break;
                case "flight":
                    profile = AppSettings.FlightModeId;
                    break;
                case "racing":
                    profile = AppSettings.RacingModeId;
                    break;
            }
        }

        return new CommandLineOptions
        {
            ProfileId = profile,
            Start = start,
            Stop = stop,
            ExitAfterAction = exitAfter,
            Minimized = minimized,
            ShowHelp = help
        };
    }

    public static string HelpText =>
        """
        FlightLauncher command line:

          --profile flight|racing   Select mode (aliases: --mode, -p, --flight, --racing)
          --start                   Run START for the selected/active profile
          --stop                    Run STOP for the selected/active profile
          --exit                    Exit after --start/--stop completes
          --minimized               Start hidden to tray
          --help                    Show this help

        Examples:
          FlightLauncher.exe --profile flight --start --exit
          FlightLauncher.exe --racing --stop --exit
          FlightLauncher.exe -p flight --start
        """;

    private static string? TakeNext(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            return null;
        }

        i++;
        return args[i].Trim().TrimStart('/', '-');
    }

    private static string? NormalizeProfile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "flight" or "f" => AppSettings.FlightModeId,
            "racing" or "race" or "r" => AppSettings.RacingModeId,
            _ => value.Trim().ToLowerInvariant()
        };
    }
}
