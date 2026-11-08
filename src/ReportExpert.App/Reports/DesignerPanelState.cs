using System.IO;
using System.Text.Json;
using ReportExpert.Common;

namespace ReportExpert.App.Reports;

/// <summary>
/// How the designer's left panel was last left: which Report data folders are open and whether the
/// Widgets list is sorted A–Z. Kept in <c>%AppData%\ReportExpert\designer-panels.json</c> so it survives
/// switching layouts and restarting the app.
/// </summary>
internal static class DesignerPanelState
{
    private const string FileName = "designer-panels.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object Gate = new();
    private static readonly State Current = Load();

    /// <summary>Whether the Widgets panel lists every widget alphabetically instead of by category.</summary>
    public static bool WidgetsAlphabetical
    {
        get => Current.WidgetsAlphabetical;
        set
        {
            if (Current.WidgetsAlphabetical == value)
                return;
            Current.WidgetsAlphabetical = value;
            Save();
        }
    }

    /// <summary>Whether the user left a Report data folder open. Folders start collapsed.</summary>
    public static bool IsFolderExpanded(string key) =>
        Current.ExpandedDataFolders.Contains(key, StringComparer.OrdinalIgnoreCase);

    public static void SetFolderExpanded(string key, bool expanded)
    {
        if (key.Length == 0 || IsFolderExpanded(key) == expanded)
            return;

        if (expanded)
            Current.ExpandedDataFolders.Add(key);
        else
            Current.ExpandedDataFolders.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    private static State Load()
    {
        try
        {
            string path = AppConstants.GetAppDataPath(FileName);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<State>(File.ReadAllText(path), JsonOptions) ?? new State();
        }
        catch
        {
            // A damaged file just means the defaults.
        }

        return new State();
    }

    private static void Save()
    {
        try
        {
            lock (Gate)
                File.WriteAllText(AppConstants.GetAppDataPath(FileName), JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch
        {
            // Remembering panel state must never break the designer.
        }
    }

    private sealed class State
    {
        public bool WidgetsAlphabetical { get; set; }

        public List<string> ExpandedDataFolders { get; set; } = [];
    }
}
