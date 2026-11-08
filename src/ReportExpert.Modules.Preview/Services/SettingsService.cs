using System.IO;
using System.Text.Json;
using ReportExpert.Common;
using ReportExpert.Modules.Preview.Models;
using ReportExpert.Storage;

namespace ReportExpert.Modules.Preview.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> in the REX SQLite database (<see cref="RexDatabase"/>).
/// A <c>settings.json</c> from an older REX is imported the first time. Unreadable data resets to
/// defaults so the app can still start.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsPath;
    private AppSettings _settings = new();

    public AppSettings Settings => _settings;

    public SettingsService()
    {
        _settingsPath = AppConstants.GetAppDataPath(AppConstants.SettingsFileName);
        Load();
    }

    public void Load()
    {
        try
        {
            var database = RexDatabase.Default;
            if (!database.HasKey(RexStorageKeys.AppSettings) && File.Exists(_settingsPath))
            {
                // One-time import of the settings file written by REX 3.0 and earlier.
                var legacy = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new AppSettings();
                database.SetJson(RexStorageKeys.AppSettings, legacy);
            }

            _settings = database.GetJson<AppSettings>(RexStorageKeys.AppSettings) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Prefer defaults over crashing startup on a corrupt settings file.
            _settings = new AppSettings();
        }
    }

    public void Save() => RexDatabase.Default.SetJson(RexStorageKeys.AppSettings, _settings);
}
