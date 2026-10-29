using System.Text.Json;

namespace Snap;

/// Where the data lives: %LOCALAPPDATA%\Snap\Snap.db, next to settings.json.
public static class AppPaths
{
    /// True when APP_STORE points somewhere else (tests, snapshots, seed data).
    public static bool IsOverridden => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APP_STORE"));

    /// Test runs only ever touch files in %TEMP%.
    public static bool IsTestStore =>
        IsOverridden && Path.GetFullPath(Store).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase);

    public static string Folder
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snap");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string Store =>
        IsOverridden ? Environment.GetEnvironmentVariable("APP_STORE")! : Path.Combine(Folder, "Snap.db");

    /// Test runs keep their preferences next to the test store, so they never change the real ones.
    public static string Prefs => IsOverridden ? Store + ".settings.json" : Path.Combine(Folder, "settings.json");
}

/// Small preferences, saved as JSON. Unpackaged apps have no ApplicationData.LocalSettings.
public sealed class Prefs
{
    public string Theme { get; set; } = "system";
    public string Accent { get; set; } = UI.Theme.DefaultId;
    public string LastPage { get; set; } = "shortcuts";
    public Dictionary<Snap.Core.SnapCommand, Snap.Core.KeyCombo?> Shortcuts { get; set; } = Snap.Core.ShortcutMap.Defaults();
    public double WideFraction { get; set; } = Snap.Core.SnapLayout.DefaultWide;
    /// Space around snapped windows in epx, scaled by each display's DPI when a window moves.
    public int Gap { get; set; }

    private static Prefs? _current;
    public static Prefs Current => _current ??= Load();

    private static Prefs Load()
    {
        try { return JsonSerializer.Deserialize<Prefs>(File.ReadAllText(AppPaths.Prefs)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        try { File.WriteAllText(AppPaths.Prefs, JsonSerializer.Serialize(this)); }
        catch (IOException e) { Console.Error.WriteLine($"Prefs not saved: {e.Message}"); }
    }
}
