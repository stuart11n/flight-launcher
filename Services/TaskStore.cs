using System.Text.Json;
using SimpitLauncher.Models;

namespace SimpitLauncher.Services;

public sealed class TaskStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;

    public TaskStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "SimpitLauncher");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "tasks.json");
        MigrateLegacySettings(appData, _settingsPath);
    }

    public string SettingsPath => _settingsPath;

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            var seeded = DefaultTasks.CreateSettings();
            Save(seeded);
            return seeded;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            var migrated = Normalize(settings);
            if (migrated)
            {
                Save(settings);
            }

            return settings;
        }
        catch
        {
            var fallback = DefaultTasks.CreateSettings();
            Save(fallback);
            return fallback;
        }
    }

    public void Save(AppSettings settings)
    {
        settings.EnsureModes();
        // Do not persist legacy flat list.
        settings.Tasks = null;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private static void MigrateLegacySettings(string appData, string newSettingsPath)
    {
        if (File.Exists(newSettingsPath))
        {
            return;
        }

        var legacyPath = Path.Combine(appData, "FlightLauncher", "tasks.json");
        if (!File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            File.Copy(legacyPath, newSettingsPath);
        }
        catch
        {
            // First-run seed will create defaults if copy fails.
        }
    }

    /// <returns>True if settings were changed and should be re-saved.</returns>
    private static bool Normalize(AppSettings settings)
    {
        var changed = false;

        if (settings.Modes.Count == 0)
        {
            var legacyTasks = settings.Tasks is { Count: > 0 }
                ? settings.Tasks
                : DefaultTasks.Create();

            settings.Modes =
            [
                new ModeConfig
                {
                    Id = AppSettings.FlightModeId,
                    Name = "Flight",
                    Tasks = legacyTasks
                },
                new ModeConfig
                {
                    Id = AppSettings.RacingModeId,
                    Name = "Racing",
                    Tasks = []
                }
            ];
            settings.ActiveModeId = AppSettings.FlightModeId;
            changed = true;
        }
        else
        {
            var before = settings.Modes.Count;
            settings.EnsureModes();
            if (settings.Modes.Count != before)
            {
                changed = true;
            }

            // If Flight was present but empty and legacy tasks exist, migrate into Flight.
            var flight = settings.Modes.First(m =>
                string.Equals(m.Id, AppSettings.FlightModeId, StringComparison.OrdinalIgnoreCase));
            if (flight.Tasks.Count == 0 && settings.Tasks is { Count: > 0 })
            {
                flight.Tasks = settings.Tasks;
                changed = true;
            }
        }

        if (string.IsNullOrWhiteSpace(settings.ActiveModeId))
        {
            settings.ActiveModeId = AppSettings.FlightModeId;
            changed = true;
        }

        // Older Shelly rows kept the IP only in startUrl/stopUrl after kind conversion.
        foreach (var mode in settings.Modes)
        {
            foreach (var task in mode.Tasks)
            {
                if (task.TryMigrateShellyIpFromUrls())
                {
                    changed = true;
                }
            }
        }

        return changed;
    }
}
