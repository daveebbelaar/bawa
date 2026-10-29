# Where a feature opens

Code for each row of the surface table in [SKILL.md](SKILL.md). It is
C# 14 and XAML for WinUI 3 (Windows App SDK 2.x), the `new-windows-app`
template's stack. `Item` is the template's model in `<App>.Core`, with a
`DueDate` field added for the flyout example. Spacing uses the template's
tokens (`UI/Space.xaml`), so `tools/grid-lint.ps1` passes.

## Contents

- Inline
- Detail pane
- Flyout
- TeachingTip
- Side pane
- ContentDialog
- New window
- Navigation page
- Tray icon flyout
- Confirmation
- Error dialog
- Status instead of a dialog
- Unsaved changes

## Inline

Use for one value on something already on screen. No surface opens, so
nothing needs closing. Save on Enter or when focus leaves the field. A
`TwoWay` `x:Bind` on `TextBox.Text` writes back when focus leaves.

```xml
<TextBox Text="{x:Bind Item.Title, Mode=TwoWay}" KeyDown="OnTitleKeyDown" />
```

```csharp
private void OnTitleKeyDown(object sender, KeyRoutedEventArgs e)
{
    if (e.Key != VirtualKey.Enter) return;
    List.Focus(FocusState.Keyboard);   // leaving the field saves it
    e.Handled = true;
}
```

## Detail pane

Use for details of the selection while people keep working in the list.
It stays open across selections and shows "No selection" when nothing is
selected. The template's `ItemsPage` is this shape: the list on the left,
the selected item in a card on the right. A detail pane people want to
hide gets a toggle in the command bar.

## Flyout

Use for a few related choices tied to one control. It closes when people
click outside, press Esc, or pick an item, and it keeps their changes.
Only an explicit Cancel throws them away. It never warns, never holds a
warning, and opens one at a time.

```xml
<Button AutomationProperties.Name="Due date" ToolTipService.ToolTip="Set the due date">
    <FontIcon Glyph="&#xE787;" />  <!-- Calendar -->
    <Button.Flyout>
        <Flyout Placement="Bottom">
            <CalendarView SelectionMode="Single" SelectedDatesChanged="OnDueDatePicked" />
        </Flyout>
    </Button.Flyout>
</Button>
```

```csharp
private void OnDueDatePicked(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs e)
{
    if (e.AddedDates.Count > 0) Item.DueDate = e.AddedDates[0];
}
```

For a field with a date, `CalendarDatePicker` is this flyout already built.
A list of commands is a `MenuFlyout`, not a `Flyout`.

## TeachingTip

Use to point out one new or hidden feature, once, next to the control it
is about. It is not for errors or for anything people must do. Show it
the first time it helps, then never again: keep a flag in `Prefs`.

```xml
<TeachingTip
    x:Name="StarTip"
    Target="{x:Bind StarButton}"
    Title="Star what matters"
    Subtitle="Starred items also show on the Starred page."
    PreferredPlacement="Bottom" />
```

```csharp
if (!Prefs.Current.SawStarTip)
{
    StarTip.IsOpen = true;
    Prefs.Current.SawStarTip = true;
    Prefs.Current.Save();
}
```

## Side pane

Use when people give input again and again and watch the result (find and
replace, filters, a color or format pane), or keep a tool open beside
their work. It sits in the window, next to the content, and does not
cover it. Its title is a noun ("Filters"), and its command says "Show
filters", never "pane".

```xml
<SplitView
    x:Name="FilterSplit"
    DisplayMode="Inline"
    PanePlacement="Right"
    OpenPaneLength="320"
    IsPaneOpen="False">
    <SplitView.Pane>
        <local:FilterPane />
    </SplitView.Pane>
    <Frame x:Name="ContentFrame" />
</SplitView>
```

Prefer sliders, number boxes, and combo boxes over typed values in a side
pane.

A Spotlight-style quick-capture window, a corner card, a countdown, or an
overlay is a different kind: a small window with no title bar that stays
on top and closes when it loses focus. Recipe: Call the Skill tool with
`os-integrations`.

## ContentDialog

Use for one short task that must finish or cancel before going on: a few
fields, one screen, no navigation inside. It blocks its own window only.
In a desktop app, set `XamlRoot`, or `ShowAsync` throws. Set `Style` to
`DefaultContentDialogStyle` for the Windows 11 look.

```csharp
var name = new TextBox { Header = "Name", Text = project.Name };
var dialog = new ContentDialog
{
    XamlRoot = XamlRoot,
    Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
    Title = "Rename project",
    Content = name,
    PrimaryButtonText = "Rename",
    CloseButtonText = "Cancel",
    DefaultButton = ContentDialogButton.Primary,   // Enter renames, Esc cancels
};
name.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = name.Text.Trim().Length > 0;
if (await dialog.ShowAsync() == ContentDialogResult.Primary) project.Name = name.Text.Trim();
```

- The primary button names the action, and Cancel is the close button.
  At most three buttons: primary, secondary, close.
- Open at a size that fits the content. A dialog that needs to scroll a
  long list wants to be a window.
- Other windows of the app stay usable while the dialog is open.
- People repeat the input and watch results? Use a side pane instead.

A dialog that grows a second screen, a navigation pane, or a scroll of
settings wants to be a window.

## New window

Use for a long or self-contained task, or something people keep open next
to the main window: an editor, a large preview. Each window remembers its
size and position.

```csharp
// ItemWindow is a Window with its own XAML, like MainWindow.
var window = new ItemWindow(item.Id);
window.SizeAndCenter(640, 480);   // UI/WindowBehavior.cs, in epx
window.Activate();
```

Save `AppWindow.Position` and `AppWindow.Size` in `Prefs` from the
`AppWindow.Closing` event, and restore them the next time the window
opens.

## Navigation page

A place people come back to (Inbox, Today, a project) is a navigation
page. One item of that list opens in the detail pane, not in a dialog. Add
the page to the `SidebarPage` enum in `AppModel.cs`: the navigation pane
lists it, and snapshot mode and the self-test visit it.

## Tray icon flyout

Use for quick actions or status while the main window is closed: a timer,
a recorder, a clipboard tool. Select the icon to open a small flyout with
the two or three things people do most. Right-click opens a menu with
"Open <App>", "Settings", and "Exit". The app keeps running in the tray
only when the brief says so; the first time the main window closes, say
so in the app. WinUI has no tray control. Recipe: Call the Skill tool with
`os-integrations`.

## Confirmation

Use only for a rare action that destroys data and cannot be undone. For
undoable actions, act at once and offer Undo. The title asks the question,
and the primary button repeats the verb.

When people chose the action on purpose (Empty the bin from a menu), make
the primary button the default, so Enter confirms what they asked for. Make
Cancel the default only when the destructive button is one people did not
ask for, such as "Don't save" when closing an edited document.

```csharp
var dialog = new ContentDialog
{
    XamlRoot = XamlRoot,
    Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
    Title = "Permanently delete 3 tasks?",
    Content = "You can't undo this action.",
    PrimaryButtonText = "Delete",
    CloseButtonText = "Cancel",
    DefaultButton = ContentDialogButton.Primary,
};
if (await dialog.ShowAsync() == ContentDialogResult.Primary) DeleteForever(selected);
```

For a delete button in a command bar, a confirm flyout on the button is
lighter than a dialog: the flyout holds one line of text and one "Delete"
button, and clicking outside cancels.

## Error dialog

Use for a problem people must act on now. The title says what happened.
The content, if any, says what to do.

- Never at launch, never only to inform, and never for a common delete
  that Undo covers.
- Esc cancels. Avoid "OK" except for a purely informational dialog.
- A dialog that repeats for a task people do often gets a "Don't show
  this again" check box in its content.

```csharp
var dialog = new ContentDialog
{
    XamlRoot = XamlRoot,
    Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
    Title = "Can't export",
    Content = "The folder is read-only. Choose a folder you can save to.",
    PrimaryButtonText = "Choose another folder",
    CloseButtonText = "Cancel",
    DefaultButton = ContentDialogButton.Primary,
};
if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ChooseFolderAsync();
```

## Status instead of a dialog

Information that needs no action goes where it applies. Offline, show the
cached content and an InfoBar at the top of the page.

```xml
<InfoBar
    IsOpen="{x:Bind Model.IsOffline, Mode=OneWay}"
    IsClosable="False"
    Severity="Informational"
    Title="Offline"
    Message="Changes save when you reconnect." />
```

Severity `Success`, `Warning`, and `Error` change the color and the icon.
An InfoBar with an action gets a button in `ActionButton` ("Retry"), and
one people can dismiss sets `IsClosable="True"`.

## Unsaved changes

Ask before closing only when people would lose what they typed. Offer to
keep it.

```csharp
private async void OnCancel(object sender, RoutedEventArgs e)
{
    if (Draft.Length == 0) { Close(); return; }
    var dialog = new ContentDialog
    {
        XamlRoot = Content.XamlRoot,
        Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        Title = "Discard this note?",
        PrimaryButtonText = "Discard",
        CloseButtonText = "Keep editing",
        DefaultButton = ContentDialogButton.Close,   // Enter keeps the note
    };
    if (await dialog.ShowAsync() == ContentDialogResult.Primary) Close();
}
```

The window's close button runs the same check: handle `AppWindow.Closing`,
set `args.Cancel = true` while a draft exists, and show the same dialog.
