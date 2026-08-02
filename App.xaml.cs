using SimpitLauncher.Services;
using Microsoft.UI.Xaml;

namespace SimpitLauncher;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }
    public static CommandLineOptions LaunchOptions { get; private set; } = new();

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Unpackaged WinUI: custom switches come from the process command line.
        var argv = Environment.GetCommandLineArgs().Skip(1).ToArray();
        LaunchOptions = CommandLineOptions.Parse(argv);

        // One-shot elevated helper (firewall/Defender COM+WMI). No window.
        if (!string.IsNullOrWhiteSpace(LaunchOptions.ElevatedJob))
        {
            var code = WindowsProtectionService.ExecuteElevatedJob(LaunchOptions.ElevatedJob);
            Environment.Exit(code);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Activate();

        if (LaunchOptions.Minimized)
        {
            MainWindow.HideToTrayFromLaunch();
        }
    }
}
