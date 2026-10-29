# Accessibility audit

How to check step 7 of [SKILL.md](SKILL.md) on a running build, and the
WinUI properties that fix what it finds. Standard controls do most of this
on their own. Custom controls need the work.

## Contents

- Run the audit
- Keyboard
- Narrator
- Custom controls
- Color and contrast
- Motion and transparency
- Text size

## Run the audit

1. Build and open the app: `./build.ps1 -Open`.
2. Open Accessibility Insights for Windows (free, from
   accessibilityinsights.io). Run FastPass on each new view: the automated
   checks and the tab stops check.
3. Put the mouse away. Run the whole flow with Tab, Shift+Tab, the arrow
   keys, Enter, Esc, and Space. Press Alt to see access keys.
4. Turn on Narrator with Ctrl+Windows+Enter. Move with the Narrator key
   (Caps Lock or Insert) plus the Left and Right arrows, and with Tab. Run
   the flow again, listening. Turn it off with the same keys.
5. Turn on, one at a time: a contrast theme (Left Alt+Left Shift+Print
   Screen, or Settings > Accessibility > Contrast themes), Settings >
   Accessibility > Visual effects > Animation effects off and Transparency
   effects off, and Settings > Accessibility > Text size at 200%. Look at
   each new view in light and dark mode.

Done when FastPass shows no failures for the new views and both runs
finish the flow without the mouse.

## Keyboard

- Every step of the flow is reachable with the keyboard, in reading order.
  Fix the order with the layout order first; `TabIndex` is the last resort.
- The default button responds to Enter, Cancel to Esc. In a ContentDialog,
  set `DefaultButton`.
- A new item gets focus so people can type at once.
- Commands people use often get an access key (`AccessKey="N"`), shown
  when people press Alt.
- An app with several panes moves between them with F6 and Shift+F6.

```csharp
// The template's ItemsPage does this: a new item gets focus in its title field.
Model.AddItem();
DispatcherQueue.TryEnqueue(() => TitleBox.Focus(FocusState.Programmatic));
```

## Narrator

- The name matches the words on screen. A control with visible text uses
  that text. An icon-only button gets `AutomationProperties.Name` with
  the same words its label would have; the tooltip says what it does.
- A list row reads as one element, by name. Narrator falls back to the
  item's `ToString()`, which is the class name unless you override it.
  Set the name on the root of the item template.
- The row's actions are in its `ContextFlyout`, which Shift+F10 and the
  Menu key open.
- Decorative images stay silent.

```xml
<Button AutomationProperties.Name="New task" ToolTipService.ToolTip="Add a task to this list (Ctrl+N)">
    <FontIcon Glyph="&#xE710;" />  <!-- Add -->
</Button>

<DataTemplate x:DataType="core:Item">
    <Grid AutomationProperties.Name="{x:Bind DisplayTitle, Mode=OneWay}">
        <!-- check box, title, due date -->
    </Grid>
</DataTemplate>

<Image Source="ms-appx:///Assets/Hero.png" AutomationProperties.AccessibilityView="Raw" />
```

- Section titles are headings, so Narrator can jump between them:
  `AutomationProperties.HeadingLevel="Level2"`.
- When content changes without a click (a sync finishes, a filter empties
  the list), raise a notification:

```csharp
using Microsoft.UI.Xaml.Automation.Peers;

void Announce(UIElement element, string text)
{
    var peer = FrameworkElementAutomationPeer.FromElement(element)
        ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
    peer.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted,
        AutomationNotificationProcessing.ImportantMostRecent, text, "status");
}

Announce(List, "No tasks left");
```

- A chart gets a one-line summary as its name ("Spending rose 12% in
  October").
- Voice access says what people see: the visible label and the
  automation name are the same words.
- No view that hides itself on a timer is the only way to act. An Undo
  InfoBar also has Ctrl+Z.

## Custom controls

Build on a standard control first. A dial is a Slider with a new template:
Narrator, the arrow keys, and Voice access come for free.

A control drawn from scratch answers four questions for Narrator: what it
is (name and control type), what it is set to (value), what people can do
(keyboard and patterns), and what changed (events). A control that only
works with a drag also works with the arrow keys, so the keyboard, Narrator,
and Voice access reach it.

```csharp
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Windows.System;

public sealed partial class VolumeDial : UserControl
{
    public VolumeDial()
    {
        InitializeComponent();
        IsTabStop = true;                       // reachable with Tab, shows the focus rectangle
        AutomationProperties.SetName(this, "Volume");
    }

    public double Level { get; private set; }   // 0 to 1

    public void SetLevel(double value)
    {
        var old = Level;
        Level = Math.Clamp(value, 0, 1);
        Redraw();
        // Narrator hears the new value.
        FrameworkElementAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(
            RangeValuePatternIdentifiers.ValueProperty, old * 100, Level * 100);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Up or VirtualKey.Right: SetLevel(Level + 0.1); break;
            case VirtualKey.Down or VirtualKey.Left: SetLevel(Level - 0.1); break;
            case VirtualKey.Home: SetLevel(0); break;
            case VirtualKey.End: SetLevel(1); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new VolumeDialPeer(this);
}

/// Tells UI Automation the dial is a slider with a value from 0 to 100.
public sealed partial class VolumeDialPeer(VolumeDial owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
    protected override string GetClassNameCore() => nameof(VolumeDial);
    protected override object GetPatternCore(PatternInterface pattern) =>
        pattern == PatternInterface.RangeValue ? this : base.GetPatternCore(pattern);

    public double Minimum => 0;
    public double Maximum => 100;
    public double Value => owner.Level * 100;
    public double SmallChange => 10;
    public double LargeChange => 25;
    public bool IsReadOnly => false;
    public void SetValue(double value) => owner.SetLevel(value / 100);
}
```

A two-axis control (a color square, a position pad) moves with all four
arrow keys by a fixed step that stops at the edges, and its context menu
offers "Move up", "Move down", "Move left", and "Move right".

## Color and contrast

- Status color always comes with an icon or a word.
- Body text contrast is at least 4.5 to 1 against its background, in light
  and dark mode. Aim for 7 to 1 for small text in custom colors.
- Use theme brushes (`{ThemeResource TextFillColorPrimaryBrush}`,
  `CardBackgroundFillColorDefaultBrush`). They follow light, dark, and
  contrast themes. A hard-coded color ignores the contrast theme.
- A custom brush gets a `HighContrast` entry that uses system colors:

```xml
<ResourceDictionary.ThemeDictionaries>
    <ResourceDictionary x:Key="Default">
        <SolidColorBrush x:Key="PriorityBrush" Color="#C42B1C" />
    </ResourceDictionary>
    <ResourceDictionary x:Key="HighContrast">
        <SolidColorBrush x:Key="PriorityBrush" Color="{ThemeResource SystemColorHighlightColor}" />
    </ResourceDictionary>
</ResourceDictionary.ThemeDictionaries>
```

Code that draws its own pixels checks
`new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast`.

## Motion and transparency

Windows' Animation effects setting reaches the app as
`UISettings.AnimationsEnabled`. When it is false, replace slides, springs,
and parallax with a fade or no animation. Never animate a blur or a change
in depth (scale toward the viewer) then. Never make people wait for an
animation: it can be interrupted.

```csharp
var ui = new Windows.UI.ViewManagement.UISettings();
var transition = ui.AnimationsEnabled
    ? new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight }
    : (NavigationTransitionInfo)new SuppressNavigationTransitionInfo();
ContentFrame.Navigate(typeof(DetailPage), item.Id, transition);
```

Transparency effects off reaches the app as
`UISettings.AdvancedEffectsEnabled`. Mica and Acrylic fall back to a solid
color on their own. A custom see-through surface also gets a solid
fallback: `AcrylicBrush.FallbackColor`, or a check of that property.

## Text size

- Windows body text is 14 epx (Segoe UI Variable) and nothing is under
  12 epx. Use the type ramp styles (`BodyTextBlockStyle`,
  `CaptionTextBlockStyle`, `SubtitleTextBlockStyle`), not fixed sizes.
- Settings > Accessibility > Text size scales text up to 225%. TextBlock
  follows it (`IsTextScaleFactorEnabled` is true by default). Layouts
  wrap or scroll at 200%, never clip. Fixed `Height` on a row of text is
  the usual bug.
- Trimmed text shows its full value in a tooltip.

```xml
<TextBlock
    Text="{x:Bind Item.Title, Mode=OneWay}"
    TextTrimming="CharacterEllipsis"
    ToolTipService.ToolTip="{x:Bind Item.Title, Mode=OneWay}" />
```

An app people read in (notes, articles, mail) also zooms its reading text
with Ctrl+Plus, Ctrl+Minus, and Ctrl+0, as Notepad and Edge do, with View
> Zoom > Zoom in, Zoom out, and Restore default zoom. Keep the scale in
`Prefs` (`AppPaths.cs`):

```csharp
// In the reading page. Add `public double TextScale { get; set; } = 1;` to Prefs.
private void AddZoomKeys()
{
    AddZoomKey((VirtualKey)187, 0.1);        // + next to Backspace (VK_OEM_PLUS)
    AddZoomKey(VirtualKey.Add, 0.1);         // + on the number pad
    AddZoomKey((VirtualKey)189, -0.1);       // - (VK_OEM_MINUS)
    AddZoomKey(VirtualKey.Subtract, -0.1);
    AddZoomKey(VirtualKey.Number0, 0);       // 0 restores
}

private void AddZoomKey(VirtualKey key, double step)
{
    var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
    accelerator.Invoked += (_, args) =>
    {
        var prefs = Prefs.Current;
        prefs.TextScale = step == 0 ? 1 : Math.Clamp(prefs.TextScale + step, 0.8, 2);
        prefs.Save();
        ReadingText.FontSize = 14 * prefs.TextScale;
        args.Handled = true;
    };
    KeyboardAccelerators.Add(accelerator);
}
```
