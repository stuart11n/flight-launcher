using FlightLauncher.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace FlightLauncher;

public sealed partial class MainWindow : Window
{
    private TrayService? _tray;
    private bool _exitRequested;
    private MainPage? _page;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(980, 1560));

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

        AppWindow.Closing += AppWindow_Closing;
        AppWindow.Changed += AppWindow_Changed;
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exitRequested || _tray is null)
        {
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
        _tray?.Dispose();
        _tray = null;
        Close();
    }

    private void HideToTray()
    {
        AppWindow.Hide();
    }

    private void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Restore();
        }
    }

    private void ExitFromTray()
    {
        _exitRequested = true;
        _tray?.Dispose();
        _tray = null;
        Close();
    }
}
