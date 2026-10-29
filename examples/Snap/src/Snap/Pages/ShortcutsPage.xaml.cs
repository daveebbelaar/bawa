using Snap.Core;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Snap.Pages;

public sealed partial class ShortcutsPage : Page
{
    private readonly Dictionary<SnapCommand, SettingsCard> _cards = [];

    public ShortcutsPage()
    {
        InitializeComponent();
        foreach (var command in SnapCommands.All)
        {
            var card = new SettingsCard { Content = new ShortcutButton(command) };
            _cards[command] = card;
            Cards.Children.Add(card);
        }
        Refresh();
        App.Model.ShortcutsChanged += Refresh;
        Unloaded += (_, _) => App.Model.ShortcutsChanged -= Refresh;
    }

    private void Refresh()
    {
        var model = App.Model;
        foreach (var (command, card) in _cards)
        {
            card.Header = command.Title(model.Wide);
            card.HeaderIcon = LayoutGlyph(command, model.Wide);
            var failed = model.Hotkeys.Failed.Contains(command);
            if (failed) card.Description = "Another app uses this shortcut.";
            else card.ClearValue(SettingsCard.DescriptionProperty);
            ((ShortcutButton)card.Content).Refresh();
        }
        FailedBar.IsOpen = model.Hotkeys.Failed.Count > 0;
    }

    /// A small drawing of the screen with the zone the command fills, like a snap layout.
    /// Commands with no zone (displays, restore) use a Segoe Fluent Icons glyph.
    public static IconElement LayoutGlyph(SnapCommand command, double wide)
    {
        if (command.Zone(wide) is not var (left, width))
            return new FontIcon { Glyph = char.ConvertFromUtf32(command == SnapCommand.Restore ? 0xE7A7 : 0xE7F4) };   // Undo, TVMonitor
        // EvenOdd: the outer rect minus the inner one is the screen's frame; the zone inside fills again.
        var shape = new GeometryGroup { FillRule = FillRule.EvenOdd };
        shape.Children.Add(new RectangleGeometry { Rect = new Rect(0, 1, 20, 16) });
        shape.Children.Add(new RectangleGeometry { Rect = new Rect(1.5, 2.5, 17, 13) });
        shape.Children.Add(new RectangleGeometry { Rect = new Rect(3 + 14 * left, 4, 14 * width, 10) });
        return new PathIcon { Data = shape };
    }
}
