using FlightLauncher.Models;
using FlightLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FlightLauncher.Dialogs;

public sealed partial class EditTaskDialog : ContentDialog
{
    private readonly Window _ownerWindow;
    private readonly TaskRunner _runner = new();
    private TaskEntry _entry;
    private bool _testing;

    public EditTaskDialog(Window ownerWindow, TaskEntry? existing = null, TaskKind? forceKind = null)
    {
        _ownerWindow = ownerWindow;
        _entry = existing is null
            ? new TaskEntry { Kind = forceKind ?? TaskKind.Executable }
            : Clone(existing);

        if (forceKind is not null && existing is null)
        {
            _entry.Kind = forceKind.Value;
            ApplyBuiltinDefaults(_entry);
        }

        InitializeComponent();
        Title = existing is null ? "Add task" : "Edit task";
        LoadFromEntry();
        UpdatePanels();
        UpdateTestActionHelp();
    }

    public TaskEntry Result => _entry;

    private void LoadFromEntry()
    {
        NameBox.Text = _entry.Name;
        SelectComboByTag(KindBox, _entry.Kind.ToString());
        PathBox.Text = _entry.Path;
        ArgsBox.Text = _entry.Arguments;
        AdminBox.IsChecked = _entry.RunAsAdministrator;
        KillBeforeBox.IsChecked = _entry.KillBeforeLaunch;
        KillBeforeForceBox.IsChecked = _entry.KillBeforeLaunchForce;
        KillImageBox.Text = _entry.KillImageName;
        SelectComboByTag(StopModeBox, _entry.StopMode.ToString());
        StopImageBox.Text = _entry.StopImageName;
        StopCommandBox.Text = _entry.StopCommand;
        StartUrlBox.Text = _entry.StartUrl;
        StopUrlBox.Text = _entry.StopUrl;
        SelectComboByTag(BuiltinBox, _entry.BuiltinAction.ToString());
        GpuWattsBox.Value = _entry.GpuPowerLimitWatts <= 0 ? 352 : _entry.GpuPowerLimitWatts;
        GpuStopWattsBox.Value = _entry.GpuStopPowerLimitWatts <= 0 ? 200 : _entry.GpuStopPowerLimitWatts;
        UpdateKillForceEnabled();
        UpdateStopFields();
        UpdateBuiltinHelp();
    }

    private void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            args.Cancel = true;
            NameBox.Focus(FocusState.Programmatic);
            return;
        }

        ApplyToEntry();
    }

    private void ApplyToEntry()
    {
        _entry.Name = NameBox.Text.Trim();
        _entry.Kind = ParseEnum(GetSelectedTag(KindBox), TaskKind.Executable);
        _entry.Path = PathBox.Text.Trim();
        _entry.Arguments = ArgsBox.Text.Trim();
        _entry.RunAsAdministrator = AdminBox.IsChecked == true;
        _entry.KillBeforeLaunch = KillBeforeBox.IsChecked == true;
        _entry.KillBeforeLaunchForce = KillBeforeForceBox.IsChecked == true;
        _entry.KillImageName = KillImageBox.Text.Trim();
        _entry.StopMode = ParseEnum(GetSelectedTag(StopModeBox), StopMode.None);
        _entry.StopImageName = StopImageBox.Text.Trim();
        _entry.StopCommand = StopCommandBox.Text.Trim();
        _entry.StartUrl = StartUrlBox.Text.Trim();
        _entry.StopUrl = StopUrlBox.Text.Trim();
        _entry.BuiltinAction = ParseEnum(GetSelectedTag(BuiltinBox), BuiltinAction.None);
        _entry.GpuPowerLimitWatts = (int)(double.IsNaN(GpuWattsBox.Value) ? 352 : GpuWattsBox.Value);
        _entry.GpuStopPowerLimitWatts = (int)(double.IsNaN(GpuStopWattsBox.Value) ? 200 : GpuStopWattsBox.Value);

        if (_entry.Kind == TaskKind.Builtin && string.IsNullOrWhiteSpace(_entry.Name))
        {
            _entry.Name = BuiltinDefaultName(_entry.BuiltinAction);
        }
    }

    private async void TestStart_Click(object sender, RoutedEventArgs e) => await RunTestAsync(starting: true);

    private async void TestStop_Click(object sender, RoutedEventArgs e) => await RunTestAsync(starting: false);

    private async Task RunTestAsync(bool starting)
    {
        if (_testing)
        {
            return;
        }

        _testing = true;
        TestStartButton.IsEnabled = false;
        TestStopButton.IsEnabled = false;
        TestResultBar.IsOpen = true;
        TestResultBar.Severity = InfoBarSeverity.Informational;
        TestResultBar.Title = starting ? "Testing Start…" : "Testing Stop…";
        TestResultBar.Message = string.Empty;

        try
        {
            ApplyToEntry();
            if (string.IsNullOrWhiteSpace(_entry.Name))
            {
                _entry.Name = "Test entry";
            }

            var probe = Clone(_entry);
            probe.Enabled = true;
            var log = starting
                ? await _runner.RunStartAsync([probe])
                : await _runner.RunStopAsync([probe]);

            var message = string.IsNullOrWhiteSpace(log) ? "(no output)" : log.Trim();
            var isError = message.Contains("ERROR", StringComparison.OrdinalIgnoreCase);
            TestResultBar.Severity = isError ? InfoBarSeverity.Error : InfoBarSeverity.Success;
            TestResultBar.Title = starting ? "Test Start" : "Test Stop";
            TestResultBar.Message = message;
        }
        catch (Exception ex)
        {
            TestResultBar.Severity = InfoBarSeverity.Error;
            TestResultBar.Title = starting ? "Test Start failed" : "Test Stop failed";
            TestResultBar.Message = ex.Message;
        }
        finally
        {
            TestStartButton.IsEnabled = true;
            TestStopButton.IsEnabled = true;
            _testing = false;
        }
    }

    private void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePanels();
        UpdateTestActionHelp();
    }

    private void StopModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateStopFields();
        UpdateTestActionHelp();
    }

    private void KillBeforeBox_Changed(object sender, RoutedEventArgs e) => UpdateKillForceEnabled();

    private void BuiltinBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBuiltinHelp();
        UpdateTestActionHelp();
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            var action = ParseEnum(GetSelectedTag(BuiltinBox), BuiltinAction.None);
            NameBox.Text = BuiltinDefaultName(action);
        }
    }

    private void UpdatePanels()
    {
        var kind = ParseEnum(GetSelectedTag(KindBox), TaskKind.Executable);
        ExecutablePanel.Visibility = kind == TaskKind.Executable ? Visibility.Visible : Visibility.Collapsed;
        WebhookPanel.Visibility = kind == TaskKind.Webhook ? Visibility.Visible : Visibility.Collapsed;
        BuiltinPanel.Visibility = kind == TaskKind.Builtin ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTestActionHelp()
    {
        var kind = ParseEnum(GetSelectedTag(KindBox), TaskKind.Executable);
        if (kind == TaskKind.Builtin)
        {
            var action = ParseEnum(GetSelectedTag(BuiltinBox), BuiltinAction.None);
            TestActionHelpText.Text = action switch
            {
                BuiltinAction.DisableFirewall =>
                    "Start action: turn firewall OFF\nStop action: turn firewall ON",
                BuiltinAction.DisableRealtimeScanning =>
                    "Start action: turn Defender realtime OFF (DisableRealtimeMonitoring=true)\nStop action: turn Defender realtime ON (DisableRealtimeMonitoring=false)\nIf start fails, disable Tamper Protection first.",
                BuiltinAction.MaxCpuPerformance =>
                    "Start action: powercfg /s High performance\nStop action: powercfg /s Balanced",
                BuiltinAction.MaxGpuPerformance =>
                    "Start action: nvidia-smi -pl (start watts)\nStop action: nvidia-smi -pl (stop watts)",
                _ => "Start / stop actions depend on the system option."
            };
            TestStartButton.Content = "Test start action";
            TestStopButton.Content = "Test stop action";
            return;
        }

        if (kind == TaskKind.Webhook)
        {
            TestActionHelpText.Text = "Start action: GET Start URL\nStop action: GET Stop URL";
        }
        else
        {
            TestActionHelpText.Text = "Start action: launch / kill-before-launch\nStop action: kill / force kill / stop command";
        }

        TestStartButton.Content = "Test start action";
        TestStopButton.Content = "Test stop action";
    }

    private void UpdateKillForceEnabled()
    {
        KillBeforeForceBox.IsEnabled = KillBeforeBox.IsChecked == true;
        if (KillBeforeBox.IsChecked != true)
        {
            KillBeforeForceBox.IsChecked = false;
        }
    }

    private void UpdateStopFields()
    {
        var mode = ParseEnum(GetSelectedTag(StopModeBox), StopMode.None);
        StopImageBox.Visibility = mode is StopMode.Kill or StopMode.ForceKill ? Visibility.Visible : Visibility.Collapsed;
        StopCommandBox.Visibility = mode == StopMode.CommandLine ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateBuiltinHelp()
    {
        var action = ParseEnum(GetSelectedTag(BuiltinBox), BuiltinAction.None);
        GpuWattsBox.Visibility = action == BuiltinAction.MaxGpuPerformance ? Visibility.Visible : Visibility.Collapsed;
        GpuStopWattsBox.Visibility = action == BuiltinAction.MaxGpuPerformance ? Visibility.Visible : Visibility.Collapsed;
        BuiltinHelpText.Text = action switch
        {
            BuiltinAction.DisableFirewall =>
                "Start action: disable firewall via INetFwPolicy2 (all profiles)\nStop action: enable firewall via INetFwPolicy2\nRequires UAC once if the app is not already elevated (no console window).",
            BuiltinAction.DisableRealtimeScanning =>
                "Start action: DisableRealtimeMonitoring=true via MSFT_MpPreference (WMI)\nStop action: DisableRealtimeMonitoring=false\nRequires UAC if not elevated. If start fails, turn off Tamper Protection in Windows Security.",
            BuiltinAction.MaxCpuPerformance =>
                "Start action: powercfg /s High performance\nStop action: powercfg /s Balanced (same command, Balanced GUID)",
            BuiltinAction.MaxGpuPerformance =>
                "Start action: nvidia-smi -pl <start watts>\nStop action: nvidia-smi -pl <stop watts> (same command)",
            _ => string.Empty
        };
    }

    private async void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_ownerWindow));
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add(".bat");
        picker.FileTypeFilter.Add(".cmd");
        picker.FileTypeFilter.Add(".ps1");
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            PathBox.Text = file.Path;
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                NameBox.Text = Path.GetFileNameWithoutExtension(file.Path);
            }
        }
    }

    private static void ApplyBuiltinDefaults(TaskEntry entry)
    {
        if (entry.Kind != TaskKind.Builtin)
        {
            return;
        }

        if (entry.BuiltinAction == BuiltinAction.None)
        {
            entry.BuiltinAction = BuiltinAction.DisableFirewall;
        }

        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            entry.Name = BuiltinDefaultName(entry.BuiltinAction);
        }
    }

    private static string BuiltinDefaultName(BuiltinAction action) => action switch
    {
        BuiltinAction.DisableFirewall => "Disable firewall",
        BuiltinAction.DisableRealtimeScanning => "Disable Realtime Threat Scanning",
        BuiltinAction.MaxCpuPerformance => "Max CPU performance",
        BuiltinAction.MaxGpuPerformance => "Max GPU performance",
        _ => "System"
    };

    private static TaskEntry Clone(TaskEntry source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Enabled = source.Enabled,
        Kind = source.Kind,
        Path = source.Path,
        Arguments = source.Arguments,
        RunAsAdministrator = source.RunAsAdministrator,
        KillBeforeLaunch = source.KillBeforeLaunch,
        KillBeforeLaunchForce = source.KillBeforeLaunchForce,
        KillImageName = source.KillImageName,
        StopMode = source.StopMode,
        StopImageName = source.StopImageName,
        StopCommand = source.StopCommand,
        StartUrl = source.StartUrl,
        StopUrl = source.StopUrl,
        BuiltinAction = source.BuiltinAction,
        GpuPowerLimitWatts = source.GpuPowerLimitWatts,
        GpuStopPowerLimitWatts = source.GpuStopPowerLimitWatts
    };

    private static void SelectComboByTag(ComboBox box, string tag)
    {
        for (var i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is ComboBoxItem item && string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedIndex = i;
                return;
            }
        }

        if (box.Items.Count > 0)
        {
            box.SelectedIndex = 0;
        }
    }

    private static string GetSelectedTag(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

    private static T ParseEnum<T>(string value, T fallback) where T : struct =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}
