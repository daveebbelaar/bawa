using Snap.Core;
using Snap.Windowing;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Snap.Pages;

/// Shows a command's shortcut. Select it, then press a new combo: Esc cancels, Backspace clears,
/// a key without Ctrl, Alt, or Win beeps. Every global hotkey is off while it records, or Windows
/// would deliver the combo as WM_HOTKEY and the button would never see it.
public sealed partial class ShortcutButton : Button
{
    private static readonly VirtualKey[] ModifierKeys =
    [
        VirtualKey.Shift, VirtualKey.Control, VirtualKey.Menu, VirtualKey.LeftWindows, VirtualKey.RightWindows,
        VirtualKey.LeftShift, VirtualKey.RightShift, VirtualKey.LeftControl, VirtualKey.RightControl, VirtualKey.LeftMenu, VirtualKey.RightMenu,
    ];

    private readonly SnapCommand _command;
    private bool _recording;

    public ShortcutButton(SnapCommand command)
    {
        _command = command;
        MinWidth = 180;
        Click += (_, _) => Start();
        // Preview: before the button turns Enter or Space into a click.
        PreviewKeyDown += OnKey;
        LostFocus += (_, _) => Stop();
        Refresh();
    }

    public bool IsRecording => _recording;

    public void Refresh()
    {
        if (_recording) return;
        Content = App.Model.Shortcuts[_command]?.ToString() ?? "Not set";
        ToolTipService.SetToolTip(this, $"Change the shortcut for {_command.Title(App.Model.Wide)}");
    }

    public void Start()
    {
        if (_recording) return;
        _recording = true;
        App.Model.SuspendHotkeys();
        Content = "Press a shortcut";
        Style = (Style)Application.Current.Resources["AccentButtonStyle"];
    }

    private void Stop()
    {
        if (!_recording) return;
        _recording = false;
        ClearValue(StyleProperty);
        App.Model.ResumeHotkeys();
        Refresh();
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        if (ModifierKeys.Contains(e.Key)) return;
        switch (e.Key)
        {
            case VirtualKey.Escape:
                Stop();
                return;
            case VirtualKey.Back when CurrentMods() == Mods.None:
                App.Model.SetShortcut(_command, null);
                Stop();
                return;
        }
        var combo = new KeyCombo(CurrentMods(), (int)e.Key);
        if (!combo.IsValid)
        {
            Native.MessageBeep(0);
            return;
        }
        App.Model.SetShortcut(_command, combo);
        Stop();
    }

    private static Mods CurrentMods()
    {
        static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        var mods = Mods.None;
        if (Down(VirtualKey.Control)) mods |= Mods.Ctrl;
        if (Down(VirtualKey.Menu)) mods |= Mods.Alt;
        if (Down(VirtualKey.Shift)) mods |= Mods.Shift;
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) mods |= Mods.Win;
        return mods;
    }
}
