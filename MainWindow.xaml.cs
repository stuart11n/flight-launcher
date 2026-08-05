using System.Runtime.InteropServices;
using SimpitLauncher.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace SimpitLauncher;

public sealed partial class MainWindow : Window
{
    private const int WmSetIcon = 0x0080;
    private const nint IconSmall = 0;
    private const nint IconBig = 1;
    private const int ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const int SmCxIcon = 11;
    private const int SmCyIcon = 12;
    private const int SmCxSmIcon = 49;
    private const int SmCySmIcon = 50;

    private TrayService? _tray;
    private bool _exitRequested;
    private MainPage? _page;
    private nint _iconSmall;
    private nint _iconBig;
    private bool _iconApplied;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1080, 1560));

        RootFrame.Navigated += (_, _) =>
        {
            _page = RootFrame.Content as MainPage;
        };
        RootFrame.Navigate(typeof(MainPage));

        try
        {
            _tray = new TrayService(this);
            _tray.ShowRequested += ShowFromTray;
            _tray.StartRequested += () => _ = _page?.RunStartFromTrayAsync();
            _tray.StopRequested += () => _ = _page?.RunStopFromTrayAsync();
            _tray.ExitRequested += ExitFromTray;
        }
        catch
        {
            // Tray is best-effort; app still works without it.
        }

        Activated += MainWindow_Activated;
        AppWindow.Closing += AppWindow_Closing;
        AppWindow.Changed += AppWindow_Changed;
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_iconApplied || args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        _iconApplied = true;
        ApplyWindowIcon();
        // WinUI sometimes reapplies a default after first activate — set again next tick.
        DispatcherQueue.TryEnqueue(ApplyWindowIcon);
    }

    private void ApplyWindowIcon()
    {
        var iconPath = ResolveAppIconPath();
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
        {
            return;
        }

        // Prefer the rocket .ico file explicitly (never shortcut icons).
        AppWindow.SetIcon(iconPath);

        var hwnd = WindowNative.GetWindowHandle(this);
        if (hwnd == nint.Zero)
        {
            return;
        }

        CleanupIcons();
        LoadIconsFromFile(iconPath, out _iconBig, out _iconSmall);
        if (_iconSmall != nint.Zero)
        {
            _ = SendMessage(hwnd, WmSetIcon, IconSmall, _iconSmall);
        }

        if (_iconBig != nint.Zero)
        {
            _ = SendMessage(hwnd, WmSetIcon, IconBig, _iconBig);
        }
    }

    private static string ResolveAppIconPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"),
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty, "Assets", "AppIcon.ico")
        };
        return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
    }

    private static void LoadIconsFromFile(string iconPath, out nint large, out nint small)
    {
        var cxSm = Math.Max(16, GetSystemMetrics(SmCxSmIcon));
        var cySm = Math.Max(16, GetSystemMetrics(SmCySmIcon));
        var cx = Math.Max(32, GetSystemMetrics(SmCxIcon));
        var cy = Math.Max(32, GetSystemMetrics(SmCyIcon));

        small = LoadImage(nint.Zero, iconPath, ImageIcon, cxSm, cySm, LrLoadFromFile);
        large = LoadImage(nint.Zero, iconPath, ImageIcon, cx, cy, LrLoadFromFile);
        if (small == nint.Zero)
        {
            small = large;
        }

        if (large == nint.Zero)
        {
            large = small;
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exitRequested || _tray is null)
        {
            CleanupIcons();
            _tray?.Dispose();
            return;
        }

        args.Cancel = true;
        HideToTray();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange || _exitRequested || _tray is null)
        {
            return;
        }

        if (sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            HideToTray();
        }
    }

    public void HideToTrayFromLaunch()
    {
        // Defer until the window is ready so the first Activate doesn't fight Hide.
        DispatcherQueue.TryEnqueue(() => HideToTray());
    }

    public void ForceExit()
    {
        _exitRequested = true;
        CleanupIcons();
        _tray?.Dispose();
        _tray = null;
        Close();
    }

    private void HideToTray()
    {
        AppWindow.Hide();
    }

    private void ShowFromTray() => BringToFront();

    public void BringToFront()
    {
        AppWindow.Show();
        Activate();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Restore();
        }

        ApplyWindowIcon();
    }

    private void ExitFromTray()
    {
        _exitRequested = true;
        CleanupIcons();
        _tray?.Dispose();
        _tray = null;
        Close();
    }

    private void CleanupIcons()
    {
        if (_iconSmall != nint.Zero && _iconSmall != _iconBig)
        {
            DestroyIcon(_iconSmall);
        }

        if (_iconBig != nint.Zero)
        {
            DestroyIcon(_iconBig);
        }

        _iconSmall = nint.Zero;
        _iconBig = nint.Zero;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
