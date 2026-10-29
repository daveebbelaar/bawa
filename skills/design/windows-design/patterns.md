# Patterns

Copyable XAML and C# for WinUI 3. Every snippet uses the template's
tokens (`UI/Space.xaml`) and theme brushes. The template's `ItemsPage`
and `SettingsPage` show most of these working together.

## Window with title bar and navigation

```xml
<Window.SystemBackdrop>
    <MicaBackdrop />
</Window.SystemBackdrop>
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
    </Grid.RowDefinitions>
    <TitleBar x:Name="AppTitleBar" Title="Tasks" IsPaneToggleButtonVisible="True"
              PaneToggleRequested="OnPaneToggleRequested">
        <TitleBar.IconSource>
            <ImageIconSource ImageSource="ms-appx:///Assets/AppIcon.png" />
        </TitleBar.IconSource>
        <!-- An app with search puts it in the middle of the title bar. -->
        <AutoSuggestBox PlaceholderText="Search" QueryIcon="Find" MinWidth="320" />
    </TitleBar>
    <NavigationView x:Name="Nav" Grid.Row="1" IsBackButtonVisible="Collapsed"
                    IsPaneToggleButtonVisible="False" IsSettingsVisible="True">
        <Frame x:Name="ContentFrame" />
    </NavigationView>
</Grid>
```

```csharp
ExtendsContentIntoTitleBar = true;
SetTitleBar(AppTitleBar);
AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
```

## Page header with commands

```xml
<Grid Margin="{StaticResource PageHeaderMargin}">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <TextBlock Text="Today" VerticalAlignment="Center" Style="{StaticResource TitleTextBlockStyle}" />
    <CommandBar Grid.Column="1" Background="Transparent" DefaultLabelPosition="Right">
        <AppBarButton Icon="Add" Label="New task" Click="OnNew">
            <AppBarButton.KeyboardAccelerators>
                <KeyboardAccelerator Key="N" Modifiers="Control" />
            </AppBarButton.KeyboardAccelerators>
        </AppBarButton>
        <CommandBar.SecondaryCommands>
            <AppBarButton Icon="Sort" Label="Sort by date" />
        </CommandBar.SecondaryCommands>
    </CommandBar>
</Grid>
```

## Card

```xml
<Border Padding="{StaticResource CardPadding}"
        Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
        BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
        BorderThickness="1"
        CornerRadius="{ThemeResource OverlayCornerRadius}">
    <StackPanel Spacing="{StaticResource SpaceSm}">
        <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="This week" />
        <TextBlock Foreground="{ThemeResource TextFillColorSecondaryBrush}" Text="12 tasks done" />
    </StackPanel>
</Border>
```

## Empty state

```xml
<StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="{StaticResource SpaceSm}">
    <FontIcon Glyph="&#xE8FD;" FontSize="{StaticResource EmptyStateGlyphSize}"
              Foreground="{ThemeResource TextFillColorTertiaryBrush}" />
    <TextBlock HorizontalAlignment="Center" Style="{StaticResource SubtitleTextBlockStyle}" Text="No tasks" />
    <TextBlock HorizontalAlignment="Center" Foreground="{ThemeResource TextFillColorSecondaryBrush}"
               Text="Press Ctrl+N to add one." />
    <Button Margin="0,8,0,0" HorizontalAlignment="Center" Content="New task"
            Style="{StaticResource AccentButtonStyle}" Click="OnNew" />
</StackPanel>
```

## List with context menu and scoped keys

```xml
<ListView x:Name="List" ItemsSource="{x:Bind Model.Visible}" SelectionMode="Single">
    <ListView.KeyboardAccelerators>
        <KeyboardAccelerator Key="Delete" Invoked="OnDeleteKey" ScopeOwner="{x:Bind List}" />
        <KeyboardAccelerator Key="F2" Invoked="OnRenameKey" ScopeOwner="{x:Bind List}" />
    </ListView.KeyboardAccelerators>
    <ListView.ItemTemplate>
        <DataTemplate x:DataType="core:Todo">
            <Grid>
                <Grid.ContextFlyout>
                    <MenuFlyout>
                        <MenuFlyoutItem Text="Rename" Tag="{x:Bind}" Click="OnRename">
                            <MenuFlyoutItem.KeyboardAccelerators>
                                <KeyboardAccelerator Key="F2" IsEnabled="False" />
                            </MenuFlyoutItem.KeyboardAccelerators>
                        </MenuFlyoutItem>
                        <MenuFlyoutSeparator />
                        <MenuFlyoutItem Text="Delete" Icon="Delete" Tag="{x:Bind}" Click="OnDelete" />
                    </MenuFlyout>
                </Grid.ContextFlyout>
                <TextBlock Text="{x:Bind Title, Mode=OneWay}" TextTrimming="CharacterEllipsis"
                           ToolTipService.ToolTip="{x:Bind Title, Mode=OneWay}" />
            </Grid>
        </DataTemplate>
    </ListView.ItemTemplate>
</ListView>
```

A disabled accelerator on a flyout item shows the shortcut text without
firing twice. Flyout items are not in the visual tree, so they get no
`DataContext`: pass the row with `Tag="{x:Bind}"`.

## Undo with an InfoBar

```xml
<InfoBar x:Name="UndoBar" IsOpen="False" Severity="Informational" Message="Task deleted">
    <InfoBar.ActionButton>
        <Button Content="Undo" Click="OnUndo" />
    </InfoBar.ActionButton>
</InfoBar>
```

```csharp
private Todo? _deleted;
private async void ShowUndo(Todo todo)
{
    _deleted = todo;
    UndoBar.IsOpen = true;
    await Task.Delay(TimeSpan.FromSeconds(5));
    if (_deleted == todo) UndoBar.IsOpen = false;
}
```

## Confirmation for what cannot be undone

```csharp
var dialog = new ContentDialog
{
    XamlRoot = XamlRoot,                       // required in WinUI 3, or ShowAsync throws
    Title = "Clear the whole history?",
    Content = $"This deletes {count} items. You can't undo this.",
    PrimaryButtonText = "Clear history",
    CloseButtonText = "Cancel",
    DefaultButton = ContentDialogButton.Close,
};
if (await dialog.ShowAsync() == ContentDialogResult.Primary) Model.ClearHistory();
```

## Teaching tip for a first-time hint

```xml
<TeachingTip x:Name="ShortcutTip" Target="{x:Bind NewButton}"
             Title="Add tasks from anywhere" Subtitle="Press Ctrl+Alt+N in any app."
             PreferredPlacement="Bottom" IsLightDismissEnabled="True" />
```

Show it once, then remember it in `Prefs`.

## Restore window size

```csharp
// On close:
Prefs.Current.WindowWidth = (int)(AppWindow.Size.Width / this.Scale());
Prefs.Current.WindowHeight = (int)(AppWindow.Size.Height / this.Scale());
// On open, instead of the default size:
this.SizeAndCenter(Prefs.Current.WindowWidth, Prefs.Current.WindowHeight);
```

Store epx, not pixels, so the size survives a move to a display with
another scale.

## Respect "Animation effects"

```csharp
var animate = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
var duration = animate ? TimeSpan.FromMilliseconds(167) : TimeSpan.Zero;
```

WinUI's own control animations already follow the setting.
