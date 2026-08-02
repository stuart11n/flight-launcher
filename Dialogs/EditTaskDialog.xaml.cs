using SimpitLauncher.Models;
using SimpitLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SimpitLauncher.Dialogs;

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
            : existing.Clone();

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
        DelayBox.Value = Math.Max(0, _entry.DelaySeconds);
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
        ShellyIpBox.Text = _entry.IpAddress;
        ComPortBox.Text = _entry.ComPort;
        ComBaudBox.Value = _entry.ComBaudRate < 0 ? 0 : _entry.ComBaudRate;
        ComStartBox.Text = _entry.ComStartText;
        ComStopBox.Text = _entry.ComStopText;
        SelectComboByTag(BuiltinBox, _entry.BuiltinAction.ToString());
        DisableStopBox.IsChecked = _entry.DisableStopAction;
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

        var kind = ParseEnum(GetSelectedTag(KindBox), TaskKind.Executable);
        if (kind == TaskKind.Shelly)
        {
            var ip = NormalizeIpInput(ShellyIpBox.Text);
            if (!System.Net.IPAddress.TryParse(ip, out _))
            {
                args.Cancel = true;
                ShellyIpBox.Focus(FocusState.Programmatic);
                return;
            }

            ShellyIpBox.Text = ip;
        }

        if (kind == TaskKind.ComCommand)
        {
            var port = NormalizeComPortInput(ComPortBox.Text);
            if (!IsValidComPort(port))
            {
                args.Cancel = true;
                ComPortBox.Focus(FocusState.Programmatic);
                return;
            }

            ComPortBox.Text = port;
        }

        ApplyToEntry();
    }

    private void ApplyToEntry()
    {
        _entry.Name = NameBox.Text.Trim();
        _entry.DelaySeconds = (int)Math.Max(0, double.IsNaN(DelayBox.Value) ? 0 : DelayBox.Value);
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
        _entry.IpAddress = NormalizeIpInput(ShellyIpBox.Text);
        _entry.ComPort = NormalizeComPortInput(ComPortBox.Text);
        _entry.ComStartText = ComStartBox.Text;
        _entry.ComStopText = ComStopBox.Text;
        _entry.ComBaudRate = (int)(double.IsNaN(ComBaudBox.Value) ? 0 : Math.Max(0, ComBaudBox.Value));
        _entry.BuiltinAction = ParseEnum(GetSelectedTag(BuiltinBox), BuiltinAction.None);
        _entry.DisableStopAction = DisableStopBox.IsChecked == true;
        _entry.GpuPowerLimitWatts = (int)(double.IsNaN(GpuWattsBox.Value) ? 352 : GpuWattsBox.Value);
        _entry.GpuStopPowerLimitWatts = (int)(double.IsNaN(GpuStopWattsBox.Value) ? 200 : GpuStopWattsBox.Value);

        if (_entry.Kind == TaskKind.Builtin && string.IsNullOrWhiteSpace(_entry.Name))
        {
            _entry.Name = BuiltinDefaultName(_entry.BuiltinAction);
        }

        if (_entry.Kind == TaskKind.Shelly && string.IsNullOrWhiteSpace(_entry.Name))
        {
            _entry.Name = string.IsNullOrWhiteSpace(_entry.IpAddress) ? "Shelly" : $"Shelly {_entry.IpAddress}";
        }

        if (_entry.Kind == TaskKind.ComCommand && string.IsNullOrWhiteSpace(_entry.Name))
        {
            _entry.Name = string.IsNullOrWhiteSpace(_entry.ComPort) ? "COM command" : _entry.ComPort;
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

            var probe = _entry.Clone();
            probe.Enabled = true;
            // Test actions run immediately so you can verify the step without waiting.
            probe.DelaySeconds = 0;
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
            _testing = false;
            TestStartButton.IsEnabled = true;
            UpdateTestActionHelp();
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

    private void DisableStopBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateBuiltinHelp();
        UpdateTestActionHelp();
    }

    private void UpdatePanels()
    {
        var kind = ParseEnum(GetSelectedTag(KindBox), TaskKind.Executable);
        ExecutablePanel.Visibility = kind == TaskKind.Executable ? Visibility.Visible : Visibility.Collapsed;
        WebhookPanel.Visibility = kind == TaskKind.Webhook ? Visibility.Visible : Visibility.Collapsed;
        ShellyPanel.Visibility = kind == TaskKind.Shelly ? Visibility.Visible : Visibility.Collapsed;
        ComPanel.Visibility = kind == TaskKind.ComCommand ? Visibility.Visible : Visibility.Collapsed;
        BuiltinPanel.Visibility = kind == TaskKind.Builtin ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTestActionHelp()
    {
        var kind = ParseEnum(GetSelectedTag(KindBox), TaskKind.Executable);
        if (kind == TaskKind.Builtin)
        {
            var action = ParseEnum(GetSelectedTag(BuiltinBox), BuiltinAction.None);
            var skipStop = DisableStopBox.IsChecked == true;
            var stopNote = skipStop
                ? "\nStop action: disabled (skipped on STOP)"
                : null;
            TestActionHelpText.Text = (action switch
            {
                BuiltinAction.DisableFirewall =>
                    "Start action: turn firewall OFF\nStop action: turn firewall ON",
                BuiltinAction.DisableRealtimeScanning =>
                    "Start action: turn Defender realtime OFF (DisableRealtimeMonitoring=true)\nStop action: turn Defender realtime ON (DisableRealtimeMonitoring=false)\nIf start fails, disable Tamper Protection first.",
                BuiltinAction.DisableUsbPowerSaving =>
                    "Start action: disable USB selective suspend and per-device USB power saving\nStop action: re-enable both",
                BuiltinAction.MaxCpuPerformance =>
                    "Start action: powercfg /s High performance\nStop action: powercfg /s Balanced",
                BuiltinAction.MaxGpuPerformance =>
                    "Start action: nvidia-smi -pl (start watts)\nStop action: nvidia-smi -pl (stop watts)",
                _ => "Start / stop actions depend on the system option."
            });
            if (stopNote is not null)
            {
                // Replace the Stop action line(s) with the disabled note for clarity.
                var lines = TestActionHelpText.Text.Split('\n')
                    .Where(l => !l.StartsWith("Stop action:", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                lines.Add(stopNote.TrimStart('\n'));
                TestActionHelpText.Text = string.Join('\n', lines);
            }

            TestStartButton.Content = "Test start action";
            TestStopButton.Content = "Test stop action";
            TestStopButton.IsEnabled = !skipStop && !_testing;
            return;
        }

        if (kind == TaskKind.Shelly)
        {
            TestActionHelpText.Text =
                "Start action: GET http://<IP>/relay/0?turn=on\nStop action: GET http://<IP>/relay/0?turn=off";
        }
        else if (kind == TaskKind.ComCommand)
        {
            TestActionHelpText.Text =
                "Start/Stop: write text to \\\\.\\COMn (same as echo TEXT > \\\\.\\COMn).\nCRLF is appended if missing. Baud 0 leaves port settings unchanged.";
        }
        else if (kind == TaskKind.Webhook)
        {
            TestActionHelpText.Text = "Start action: GET Start URL\nStop action: GET Stop URL";
        }
        else
        {
            TestActionHelpText.Text = "Start action: launch / kill-before-launch\nStop action: kill / force kill / stop command";
        }

        TestStartButton.Content = "Test start action";
        TestStopButton.Content = "Test stop action";
        if (!_testing)
        {
            TestStopButton.IsEnabled = true;
        }
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
        var skipStop = DisableStopBox.IsChecked == true;
        GpuWattsBox.Visibility = action == BuiltinAction.MaxGpuPerformance ? Visibility.Visible : Visibility.Collapsed;
        GpuStopWattsBox.Visibility = action == BuiltinAction.MaxGpuPerformance && !skipStop
            ? Visibility.Visible
            : Visibility.Collapsed;
        BuiltinHelpText.Text = action switch
        {
            BuiltinAction.DisableFirewall =>
                "Start action: disable firewall via INetFwPolicy2 (all profiles)\nStop action: enable firewall via INetFwPolicy2\nRequires UAC once if the app is not already elevated (no console window).",
            BuiltinAction.DisableRealtimeScanning =>
                "Start action: DisableRealtimeMonitoring=true via MSFT_MpPreference (WMI)\nStop action: DisableRealtimeMonitoring=false\nRequires UAC if not elevated. If start fails, turn off Tamper Protection in Windows Security.",
            BuiltinAction.DisableUsbPowerSaving =>
                "Start action: disable USB selective suspend (registry + powercfg if present) and uncheck per-device USB power saving\nStop action: re-enable both\nRequires UAC if not elevated.",
            BuiltinAction.MaxCpuPerformance =>
                "Start action: powercfg /s High performance\nStop action: powercfg /s Balanced (same command, Balanced GUID)",
            BuiltinAction.MaxGpuPerformance =>
                "Start action: nvidia-smi -pl <start watts>\nStop action: nvidia-smi -pl <stop watts> (same command)",
            _ => string.Empty
        };
        if (skipStop && !string.IsNullOrEmpty(BuiltinHelpText.Text))
        {
            var lines = BuiltinHelpText.Text.Split('\n')
                .Where(l => !l.StartsWith("Stop action:", StringComparison.OrdinalIgnoreCase))
                .Append("Stop action: disabled (skipped on STOP)")
                .ToArray();
            BuiltinHelpText.Text = string.Join('\n', lines);
        }
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
        BuiltinAction.DisableUsbPowerSaving => "Disable USB power saving",
        BuiltinAction.MaxCpuPerformance => "Max CPU performance",
        BuiltinAction.MaxGpuPerformance => "Max GPU performance",
        _ => "System"
    };

    /// <summary>Accepts a raw IP, or a pasted URL; returns the IP host if valid.</summary>
    private static string NormalizeIpInput(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (System.Net.IPAddress.TryParse(text, out _))
        {
            return text;
        }

        if (!text.Contains("://", StringComparison.Ordinal) &&
            !text.Contains('/', StringComparison.Ordinal))
        {
            return text;
        }

        if (!Uri.TryCreate(text.Contains("://", StringComparison.Ordinal) ? text : $"http://{text}",
                UriKind.Absolute, out var uri))
        {
            return text;
        }

        return uri.Host;
    }

    private static string NormalizeComPortInput(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (int.TryParse(text, out var n) && n > 0)
        {
            return $"COM{n}";
        }

        if (text.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(text[3..], out n) && n > 0)
        {
            return $"COM{n}";
        }

        return text.ToUpperInvariant();
    }

    private static bool IsValidComPort(string port) =>
        port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(port.AsSpan(3), out var n) &&
        n > 0;

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
