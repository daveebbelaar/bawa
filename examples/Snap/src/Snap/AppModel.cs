using Snap.Core;
using Snap.Windowing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Snap;

/// The Settings window's pages. Snapshot mode walks through every case.
public enum SidebarPage { Shortcuts, Layout, General }

public static class SidebarPages
{
    public static string Id(this SidebarPage page) => page.ToString().ToLowerInvariant();

    public static string Title(this SidebarPage page) => page switch
    {
        SidebarPage.Shortcuts => "Shortcuts",
        SidebarPage.Layout => "Layout",
        SidebarPage.General => "General",
        _ => page.ToString(),
    };

    /// Segoe Fluent Icons glyphs, written as code points so they stay readable in source.
    public static string Glyph(this SidebarPage page) => char.ConvertFromUtf32(page switch
    {
        SidebarPage.Shortcuts => 0xE765,   // Keyboard
        SidebarPage.Layout => 0xE8A9,      // ViewAll
        _ => 0xE713,                       // Setting
    });

    public static SidebarPage? FromId(string id) =>
        Enum.GetValues<SidebarPage>().Cast<SidebarPage?>().FirstOrDefault(p => p!.Value.Id() == id);
}

/// App-wide state: the shortcuts, the layout settings, and the parts that act on Windows.
public partial class AppModel : ObservableObject, IDisposable
{
    public AppModel(bool withTrayIcon)
    {
        Shortcuts = new ShortcutMap { Bindings = new(Prefs.Current.Shortcuts) };
        Tray = new TrayHost(withTrayIcon);
        Hotkeys = new HotkeyCenter(Tray.Handle);
        Windows = new WindowManager();
        Hotkeys.RegisterAll(Shortcuts);

        Tray.Hotkey += id => { if (HotkeyCenter.FromId(id) is { } command) _ = RunAsync(command, HotkeyTarget); };
        Tray.MenuItems = MenuItems;
        Tray.MenuChosen += OnMenuChosen;
        Page = SidebarPages.FromId(Prefs.Current.LastPage) ?? SidebarPage.Shortcuts;
    }

    /// The self-test points hotkeys at its own test window, so a test never moves the person's windows.
    /// Zero (always, outside the self-test) means the window in front.
    public nint HotkeyTarget { get; set; }

    public ShortcutMap Shortcuts { get; }
    public TrayHost Tray { get; }
    public HotkeyCenter Hotkeys { get; }
    public WindowManager Windows { get; }

    /// Raised when a shortcut, the wide width, or a hotkey's registration changes.
    public event Action? ShortcutsChanged;
    public event Action? ShowSettingsRequested { add => Tray.ShowSettingsRequested += value; remove => Tray.ShowSettingsRequested -= value; }
    public event Action? QuitRequested;

    [ObservableProperty]
    public partial SidebarPage Page { get; set; }

    partial void OnPageChanged(SidebarPage value)
    {
        Prefs.Current.LastPage = value.Id();
        Save();
    }

    public double Wide
    {
        get => Prefs.Current.WideFraction;
        set
        {
            Prefs.Current.WideFraction = SnapLayout.ClampWide(value);
            Save();
            OnPropertyChanged();
            ShortcutsChanged?.Invoke();
        }
    }

    public int Gap
    {
        get => Prefs.Current.Gap;
        set
        {
            Prefs.Current.Gap = Math.Clamp(value / 2 * 2, 0, 40);
            Save();
            OnPropertyChanged();
        }
    }

    public Task<SnapResult> RunAsync(SnapCommand command, nint window = 0) =>
        RunAndExplainAsync(command, window);

    private async Task<SnapResult> RunAndExplainAsync(SnapCommand command, nint window)
    {
        var result = await Windows.RunAsync(command, Wide, Gap, window);
        // A trail for debugging real shortcuts: only written when APP_LOG is set.
        AgentLog.Trace($"SNAP {command} -> {result} (window {Windows.LastTarget:X})");
        if (result == SnapResult.Refused)
        {
            // UIPI: a normal app may not move the windows of an app running as administrator.
            Tray.SetTip("Snap can't move windows of apps running as administrator.");
            await Task.Delay(TimeSpan.FromSeconds(8));
            Tray.SetTip("Snap");
        }
        return result;
    }

    /// Sets a shortcut, takes it away from any other command, and registers the hotkeys again.
    public bool SetShortcut(SnapCommand command, KeyCombo? combo)
    {
        if (!Shortcuts.Assign(command, combo)) return false;
        Save();
        Hotkeys.RegisterAll(Shortcuts);
        ShortcutsChanged?.Invoke();
        return true;
    }

    public void SuspendHotkeys() => Hotkeys.Suspend();

    public void ResumeHotkeys()
    {
        Hotkeys.RegisterAll(Shortcuts);
        ShortcutsChanged?.Invoke();
    }

    private void Save()
    {
        Prefs.Current.Shortcuts = new(Shortcuts.Bindings);
        if (!AppPaths.IsOverridden) Prefs.Current.Save();
    }

    private const int MenuSettings = 100, MenuExit = 101;

    private IReadOnlyList<(int Id, string Text, bool Enabled)> MenuItems()
    {
        var items = SnapCommands.All
            .Select(c => (HotkeyCenter.Id(c), Shortcuts[c] is { } k ? $"{c.Title(Wide)}\t{k}" : c.Title(Wide), true))
            .ToList();
        items.Add((0, "", true));
        items.Add((MenuSettings, "Settings", true));
        items.Add((MenuExit, "Exit", true));
        return items;
    }

    private void OnMenuChosen(int id)
    {
        if (id == MenuSettings) Tray.RequestSettings();
        else if (id == MenuExit) QuitRequested?.Invoke();
        // A tray menu click made Snap the foreground app; RunAsync uses the window that was in front before.
        else if (HotkeyCenter.FromId(id) is { } command) _ = RunAsync(command);
    }

    public void Dispose()
    {
        Hotkeys.UnregisterAll();
        Windows.Dispose();
        Tray.Dispose();
    }
}
