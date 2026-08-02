using System.Collections.ObjectModel;
using FlightLauncher.Dialogs;
using FlightLauncher.Models;
using FlightLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FlightLauncher;

public sealed partial class MainPage : Page
{
    private readonly TaskStore _store = new();
    private readonly TaskRunner _runner = new();
    private readonly ObservableCollection<TaskEntry> _tasks = [];
    private AppSettings _settings = new();
    private bool _loading;
    private bool _busy;

    public MainPage()
    {
        InitializeComponent();
        TaskList.ItemsSource = _tasks;
        Loaded += MainPage_Loaded;
    }

    public async Task RunStartFromTrayAsync() => await RunStartAsync();

    public async Task RunStopFromTrayAsync() => await RunStopAsync();

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        _settings = _store.Load();
        _settings.EnsureModes();
        StartOnLoginBox.IsChecked = _settings.StartOnLogin || StartupService.IsEnabled();

        var options = App.LaunchOptions;
        if (options.ShowHelp)
        {
            AppendLog(CommandLineOptions.HelpText.Trim());
        }

        var profileId = options.ProfileId;
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            _settings.ActiveModeId = profileId;
        }

        SelectModeTab(_settings.ActiveModeId);
        ApplyModeTabLabels();
        LoadActiveModeTasks();
        _loading = false;
        var mode = _settings.GetActiveMode();
        AppendLog($"Loaded mode '{mode.Name}' ({_tasks.Count} tasks) from {_store.SettingsPath}");

        if (!string.IsNullOrWhiteSpace(options.ProfileId))
        {
            Persist();
            AppendLog($"CLI profile: {mode.Name}");
        }

        await ApplyLaunchOptionsAsync(options);
    }

    private async Task ApplyLaunchOptionsAsync(CommandLineOptions options)
    {
        if (!options.Start && !options.Stop)
        {
            return;
        }

        // Let the UI finish laying out before running long actions.
        await Task.Yield();

        if (options.Start)
        {
            AppendLog("CLI: --start");
            await RunStartAsync();
        }

        if (options.Stop)
        {
            AppendLog("CLI: --stop");
            await RunStopAsync();
        }

        if (options.ExitAfterAction)
        {
            AppendLog("CLI: --exit");
            App.MainWindow?.ForceExit();
        }
    }

    private void ModeSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_loading || sender.SelectedItem is null)
        {
            return;
        }

        Persist();
        var modeId = (sender.SelectedItem.Tag as string) ?? AppSettings.FlightModeId;
        _settings.ActiveModeId = modeId;
        LoadActiveModeTasks();
        Persist();
        AppendLog($"Switched to {_settings.GetActiveMode().Name}");
    }

    private void SelectModeTab(string modeId)
    {
        ModeSelector.SelectedItem = string.Equals(modeId, AppSettings.RacingModeId, StringComparison.OrdinalIgnoreCase)
            ? RacingTab
            : FlightTab;
    }

    private void ApplyModeTabLabels()
    {
        _settings.EnsureModes();
        foreach (var mode in _settings.Modes)
        {
            var tab = string.Equals(mode.Id, AppSettings.RacingModeId, StringComparison.OrdinalIgnoreCase)
                ? RacingTab
                : string.Equals(mode.Id, AppSettings.FlightModeId, StringComparison.OrdinalIgnoreCase)
                    ? FlightTab
                    : null;
            if (tab is null)
            {
                continue;
            }

            tab.Text = string.IsNullOrWhiteSpace(mode.Name) ? mode.Id : mode.Name;
            tab.Tag = mode.Id;
        }
    }

    private async void RenameMode_Click(object sender, RoutedEventArgs e)
    {
        var mode = _settings.GetActiveMode();
        var box = new TextBox
        {
            Header = "Profile name",
            Text = mode.Name,
            MaxLength = 40
        };

        var dialog = new ContentDialog
        {
            Title = "Rename profile",
            Content = box,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var name = box.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            AppendLog("Rename cancelled: name cannot be empty");
            return;
        }

        mode.Name = name;
        ApplyModeTabLabels();
        Persist();
        AppendLog($"Renamed profile to '{mode.Name}'");
    }

    private void LoadActiveModeTasks()
    {
        var mode = _settings.GetActiveMode();
        _tasks.Clear();
        foreach (var task in mode.Tasks)
        {
            _tasks.Add(task);
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e) => await RunStartAsync();

    private async void StopButton_Click(object sender, RoutedEventArgs e) => await RunStopAsync();

    private async Task RunStartAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SetButtonsEnabled(false);
        var modeName = _settings.GetActiveMode().Name;
        var total = _tasks.Count(t => t.Enabled);
        BeginProgress(total, $"Starting {modeName}…");
        AppendLog($"--- START ({modeName}) ---");
        var completed = 0;
        var progress = new Progress<string>(line =>
        {
            completed++;
            UpdateProgress(completed, total, line);
            AppendLog(line);
        });
        try
        {
            await _runner.RunStartAsync(_tasks, progress);
            CompleteProgress($"START complete ({modeName})");
            AppendLog($"--- START complete ({modeName}) ---");
        }
        catch (Exception ex)
        {
            CompleteProgress($"START failed: {ex.Message}");
            AppendLog($"START failed: {ex.Message}");
        }
        finally
        {
            SetButtonsEnabled(true);
            _busy = false;
        }
    }

    private async Task RunStopAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SetButtonsEnabled(false);
        var modeName = _settings.GetActiveMode().Name;
        var total = _tasks.Count(t => t.Enabled);
        BeginProgress(total, $"Stopping {modeName}…");
        AppendLog($"--- STOP ({modeName}) ---");
        var completed = 0;
        var progress = new Progress<string>(line =>
        {
            completed++;
            UpdateProgress(completed, total, line);
            AppendLog(line);
        });
        try
        {
            await _runner.RunStopAsync(_tasks, progress);
            CompleteProgress($"STOP complete ({modeName})");
            AppendLog($"--- STOP complete ({modeName}) ---");
        }
        catch (Exception ex)
        {
            CompleteProgress($"STOP failed: {ex.Message}");
            AppendLog($"STOP failed: {ex.Message}");
        }
        finally
        {
            SetButtonsEnabled(true);
            _busy = false;
        }
    }

    private void BeginProgress(int total, string status)
    {
        RunProgressBar.Minimum = 0;
        RunProgressBar.Maximum = Math.Max(total, 1);
        RunProgressBar.Value = 0;
        RunProgressBar.IsIndeterminate = total == 0;
        RunProgressText.Text = status;
    }

    private void UpdateProgress(int completed, int total, string line)
    {
        RunProgressBar.IsIndeterminate = false;
        RunProgressBar.Maximum = Math.Max(total, 1);
        RunProgressBar.Value = Math.Clamp(completed, 0, (int)RunProgressBar.Maximum);
        var summary = string.IsNullOrWhiteSpace(line) ? "Working…" : line;
        if (summary.Length > 90)
        {
            summary = summary[..87] + "…";
        }

        RunProgressText.Text = total > 0
            ? $"{completed}/{total}: {summary}"
            : summary;
    }

    private void CompleteProgress(string status)
    {
        RunProgressBar.IsIndeterminate = false;
        if (RunProgressBar.Maximum < 1)
        {
            RunProgressBar.Maximum = 1;
        }

        RunProgressBar.Value = RunProgressBar.Maximum;
        RunProgressText.Text = status;
    }

    private void SetButtonsEnabled(bool enabled)
    {
        StartButton.IsEnabled = enabled;
        StopButton.IsEnabled = enabled;
    }

    private async void AddExecutable_Click(object sender, RoutedEventArgs e) =>
        await AddOrEditAsync(null, TaskKind.Executable);

    private async void AddWebhook_Click(object sender, RoutedEventArgs e) =>
        await AddOrEditAsync(null, TaskKind.Webhook);

    private async void AddBuiltin_Click(object sender, RoutedEventArgs e) =>
        await AddOrEditAsync(null, TaskKind.Builtin);

    private async void TaskList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (TaskList.SelectedItem is TaskEntry entry)
        {
            await AddOrEditAsync(entry, null);
        }
    }

    private async Task AddOrEditAsync(TaskEntry? existing, TaskKind? forceKind)
    {
        var window = App.MainWindow;
        if (window is null)
        {
            return;
        }

        var dialog = new EditTaskDialog(window, existing, forceKind)
        {
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        if (existing is null)
        {
            _tasks.Add(dialog.Result);
        }
        else
        {
            var index = _tasks.IndexOf(existing);
            if (index >= 0)
            {
                _tasks[index] = dialog.Result;
            }
        }

        Persist();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TaskEntry entry })
        {
            _tasks.Remove(entry);
            Persist();
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TaskEntry entry })
        {
            return;
        }

        var index = _tasks.IndexOf(entry);
        if (index > 0)
        {
            _tasks.Move(index, index - 1);
            Persist();
        }
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TaskEntry entry })
        {
            return;
        }

        var index = _tasks.IndexOf(entry);
        if (index >= 0 && index < _tasks.Count - 1)
        {
            _tasks.Move(index, index + 1);
            Persist();
        }
    }

    private void TaskList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => Persist();

    private void TaskEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            Persist();
        }
    }

    private void StartOnLoginBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var enabled = StartOnLoginBox.IsChecked == true;
        try
        {
            StartupService.SetEnabled(enabled);
            _settings.StartOnLogin = enabled;
            Persist();
            AppendLog(enabled ? "Start on login enabled" : "Start on login disabled");
        }
        catch (Exception ex)
        {
            AppendLog($"Start on login failed: {ex.Message}");
            _loading = true;
            StartOnLoginBox.IsChecked = StartupService.IsEnabled();
            _loading = false;
        }
    }

    private void Persist()
    {
        _settings.EnsureModes();
        var mode = _settings.GetActiveMode();
        mode.Tasks = _tasks.ToList();
        _settings.ActiveModeId = mode.Id;
        _settings.StartOnLogin = StartOnLoginBox.IsChecked == true;
        _store.Save(_settings);
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var stamp = DateTime.Now.ToString("HH:mm:ss");
        LogBox.Text += $"[{stamp}] {line}{Environment.NewLine}";
    }
}
