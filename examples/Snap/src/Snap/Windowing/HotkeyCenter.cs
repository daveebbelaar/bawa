using Snap.Core;

namespace Snap.Windowing;

/// Registers one global hotkey per command on the tray window, and remembers which failed.
/// RegisterHotKey fails silently when another app (or a graphics driver) owns the combo;
/// the failures are what Settings shows next to each shortcut.
public sealed class HotkeyCenter(nint window)
{
    public HashSet<SnapCommand> Registered { get; } = [];
    public HashSet<SnapCommand> Failed { get; } = [];
    public bool IsSuspended { get; private set; }

    public static int Id(SnapCommand command) => (int)command + 1;
    public static SnapCommand? FromId(int id) => Enum.IsDefined(typeof(SnapCommand), id - 1) ? (SnapCommand)(id - 1) : null;

    public void RegisterAll(ShortcutMap map)
    {
        UnregisterAll();
        Failed.Clear();
        IsSuspended = false;
        foreach (var command in SnapCommands.All)
        {
            if (map[command] is not { } combo) continue;
            if (Native.RegisterHotKey(window, Id(command), (uint)combo.Mods | Native.MOD_NOREPEAT, (uint)combo.Key)) Registered.Add(command);
            else Failed.Add(command);
        }
    }

    public void UnregisterAll()
    {
        foreach (var command in Registered) Native.UnregisterHotKey(window, Id(command));
        Registered.Clear();
    }

    /// While a shortcut is being recorded, no hotkey may fire: Windows would deliver the combo
    /// as WM_HOTKEY and the recorder would never see it.
    public void Suspend()
    {
        UnregisterAll();
        IsSuspended = true;
    }
}
