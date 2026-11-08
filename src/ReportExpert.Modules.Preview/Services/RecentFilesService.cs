using System.IO;
using System.Text.Json;
using ReportExpert.Common;
using ReportExpert.Modules.Preview.Models;
using ReportExpert.Storage;

namespace ReportExpert.Modules.Preview.Services;

/// <summary>
/// Tracks recently opened RDLC paths in the REX SQLite database. A <c>recent.json</c> from an older REX
/// is imported the first time.
/// </summary>
public sealed class RecentFilesService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _recentPath;
    private readonly SettingsService _settingsService;
    private List<RecentFileEntry> _entries = [];

    public IReadOnlyList<RecentFileEntry> Entries => _entries;

    public RecentFilesService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _recentPath = AppConstants.GetAppDataPath(AppConstants.RecentFilesFileName);
        Load();
    }

    public void Load()
    {
        try
        {
            var database = RexDatabase.Default;
            const string imported = RexStorageKeys.LegacyFilesImported + ".recent-files";
            if (!database.HasKey(imported))
            {
                if (File.Exists(_recentPath))
                {
                    var legacy = JsonSerializer.Deserialize<List<RecentFileEntry>>(File.ReadAllText(_recentPath)) ?? [];
                    database.ReplaceRecentItems(RexStorageKeys.RecentFile, legacy.Select(e => new RecentItem(e.Path, System.IO.Path.GetFileName(e.Path), e.LastOpenedUtc)));
                }

                database.SetJson(imported, DateTimeOffset.UtcNow);
            }

            _entries = database.RecentItems(RexStorageKeys.RecentFile)
                .Select(item => new RecentFileEntry { Path = item.Path, LastOpenedUtc = item.OpenedUtc })
                .Where(e => File.Exists(e.Path))
                .ToList();
        }
        catch (Exception)
        {
            // Corrupt recent-file cache — start empty rather than blocking open flows.
            _entries = [];
        }
    }

    public void Add(string path)
    {
        if (!_settingsService.Settings.RememberRecentFiles)
            return;

        _entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        _entries.Insert(0, new RecentFileEntry
        {
            Path = path,
            LastOpenedUtc = DateTime.UtcNow
        });

        int max = Math.Max(1, _settingsService.Settings.MaxRecentFiles);
        if (_entries.Count > max)
            _entries = _entries.Take(max).ToList();

        Save();
    }

    public void Save() =>
        RexDatabase.Default.ReplaceRecentItems(
            RexStorageKeys.RecentFile,
            _entries.Select(e => new RecentItem(e.Path, System.IO.Path.GetFileName(e.Path), e.LastOpenedUtc)));
}
