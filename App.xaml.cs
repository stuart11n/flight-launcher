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

        // Single interactive UI instance. CLI --start/--stop (also /start /stop) may run alongside.
        var allowParallel = LaunchOptions.Start || LaunchOptions.Stop;
        if (!allowParallel && !SingleInstanceService.TryAcquire())
        {
            SingleInstanceService.SignalActivate();
            Environment.Exit(0);
            return;
        }

        // Keep the running app's taskbar identity separate from Start/Stop desktop shortcuts.
        AppIdentity.SetProcessAppUserModelId();

        MainWindow = new MainWindow();
        MainWindow.Activate();

        if (!allowParallel)
        {
            SingleInstanceService.StartActivateListener(() =>
            {
                MainWindow?.DispatcherQueue.TryEnqueue(() => MainWindow?.BringToFront());
            });
        }

        var startMinimized = LaunchOptions.Minimized;
        if (!startMinimized)
        {
            try
            {
                startMinimized = new TaskStore().Load().StartMinimized;
            }
            catch
            {
                // Settings load is best-effort for launch minimize.
            }
        }

        if (startMinimized)
        {
            MainWindow.HideToTrayFromLaunch();
        }
    }
}
