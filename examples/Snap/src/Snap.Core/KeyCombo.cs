namespace Snap.Core;

/// Modifier flags with the same values RegisterHotKey uses (MOD_ALT, MOD_CONTROL, MOD_SHIFT, MOD_WIN).
[Flags]
public enum Mods { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

/// A virtual key plus modifiers, like Ctrl+Alt+Left.
public readonly record struct KeyCombo(Mods Mods, int Key)
{
    public const int Backspace = 0x08, Enter = 0x0D, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, F1 = 0x70, F12 = 0x7B;

    public bool IsFunctionKey => Key is >= F1 and <= F12;

    /// A global shortcut needs Ctrl, Alt, or Win, except F1 to F12. Shift alone would steal typing.
    public bool IsValid => Key != 0 && ((Mods & (Mods.Ctrl | Mods.Alt | Mods.Win)) != 0 || IsFunctionKey);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Mods.HasFlag(Mods.Ctrl)) parts.Add("Ctrl");
        if (Mods.HasFlag(Mods.Alt)) parts.Add("Alt");
        if (Mods.HasFlag(Mods.Shift)) parts.Add("Shift");
        if (Mods.HasFlag(Mods.Win)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join('+', parts);
    }

    public static string KeyName(int key) => key switch
    {
        Backspace => "Backspace",
        0x09 => "Tab",
        Enter => "Enter",
        0x1B => "Esc",
        0x20 => "Space",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0x23 => "End",
        0x24 => "Home",
        Left => "Left",
        Up => "Up",
        Right => "Right",
        Down => "Down",
        0x2D => "Insert",
        0x2E => "Delete",
        >= 0x30 and <= 0x39 => ((char)key).ToString(),
        >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x60 and <= 0x69 => $"Num {key - 0x60}",
        >= F1 and <= 0x87 => $"F{key - F1 + 1}",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => $"Key {key:X2}",
    };
}

/// The shortcut for every command. One combo belongs to one command.
public sealed class ShortcutMap
{
    public Dictionary<SnapCommand, KeyCombo?> Bindings { get; set; } = Defaults();

    public static Dictionary<SnapCommand, KeyCombo?> Defaults() => new()
    {
        [SnapCommand.Left] = new KeyCombo(Mods.Ctrl | Mods.Alt, KeyCombo.Left),
        [SnapCommand.Right] = new KeyCombo(Mods.Ctrl | Mods.Alt, KeyCombo.Right),
        [SnapCommand.Maximize] = new KeyCombo(Mods.Ctrl | Mods.Alt, KeyCombo.Enter),
        [SnapCommand.LeftWide] = new KeyCombo(Mods.Ctrl | Mods.Alt, KeyCombo.Down),
        [SnapCommand.RightWide] = new KeyCombo(Mods.Ctrl | Mods.Alt, KeyCombo.Up),
        [SnapCommand.NextDisplay] = new KeyCombo(Mods.Ctrl | Mods.Alt | Mods.Shift, KeyCombo.Right),
        [SnapCommand.PreviousDisplay] = new KeyCombo(Mods.Ctrl | Mods.Alt | Mods.Shift, KeyCombo.Left),
        [SnapCommand.Restore] = new KeyCombo(Mods.Ctrl | Mods.Alt, KeyCombo.Backspace),
    };

    public KeyCombo? this[SnapCommand command] => Bindings.GetValueOrDefault(command);

    /// Sets a command's combo and takes it away from any other command. Null clears it.
    /// Returns false, and changes nothing, for a combo a global shortcut cannot use.
    public bool Assign(SnapCommand command, KeyCombo? combo)
    {
        if (combo is { IsValid: false }) return false;
        if (combo is { } c)
            foreach (var other in Bindings.Where(b => b.Key != command && b.Value == c).Select(b => b.Key).ToList())
                Bindings[other] = null;
        Bindings[command] = combo;
        return true;
    }
}

/// The frame of each window before its first snap, so Restore can put it back.
public sealed class RestoreBook
{
    private readonly Dictionary<long, Box> _frames = [];

    /// Keeps the first frame: later snaps of the same window do not overwrite it.
    public void RememberIfNew(long window, Box frame) => _frames.TryAdd(window, frame);

    /// The stored frame, forgotten once taken. Null when the window was never snapped.
    public Box? Take(long window) => _frames.Remove(window, out var frame) ? frame : null;
}
