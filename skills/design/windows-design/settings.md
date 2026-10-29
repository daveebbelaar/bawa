# Settings

Settings looks like Windows Settings: one scrolling page with a title,
group headers, and one card per setting. People already know how to use
it.

## Where it lives

- A navigation window: the gear at the bottom of the `NavigationView`
  (`IsSettingsVisible="True"`). The template's `SettingsPage` is this.
- A tray app: a normal window with the same page, opened from the tray
  menu ("Settings") and from the app's Start entry when it is already
  running.
- Windows has no Settings shortcut. Do not invent Ctrl+comma.

## Structure

```text
Settings                       TitleTextBlockStyle
Appearance                     BodyStrongTextBlockStyle, 8 epx above the cards
  [card] App theme             ComboBox: Light, Dark, Use system setting
  [card] Accent color          ComboBox with swatches
Shortcuts                      32 epx above the header
  [card] Open quick add        shortcut field
About
  [card] <App>  Version 1.2    Button: Open data folder
```

- Cards come from `CommunityToolkit.WinUI.Controls.SettingsControls`:
  `SettingsCard` for one setting, `SettingsExpander` for a setting with
  sub-settings.
- Each card: `HeaderIcon` (a Segoe Fluent Icons glyph), `Header` (sentence
  case, a noun), `Description` (one line on what it changes), and the
  control on the right.
- 4 epx between cards in a group, 32 epx above a group header, 8 below it.
- Order groups by use: the setting people change most comes first; About
  comes last.

## Controls on cards

| Setting | Control |
| --- | --- |
| On or off | `ToggleSwitch` with no header (the card has it) |
| One of three to seven | `ComboBox`, at least 200 epx wide |
| One of two | `ToggleSwitch` if it is on/off, else `ComboBox` |
| A number in a range | `Slider` with the value shown, or `NumberBox` |
| A shortcut | a shortcut field: a button that records the next combo |
| A folder | the path as text and a "Browse" button (`FolderPicker`, which needs the window handle in WinUI 3) |
| A link out | `SettingsCard` with `IsClickEnabled="True"` and `ActionIcon` |

## Behavior

- Changes apply at once. No Save or Apply button.
- Settings that need a restart say so in their description, and the app
  offers "Restart now" in an `InfoBar`.
- App theme: "Use system setting" is the default. Changing it updates the
  open windows and the caption buttons at once.
- Destructive settings ("Clear history") are a button on a card that opens
  a `ContentDialog` with the count.
- Test runs never write the real settings: the template keeps settings
  next to the `%TEMP%` test store when `APP_STORE` is set.

## Permissions and system state

Some features depend on Windows state the app does not own: launch at
login, a global shortcut another app took, a notification setting. Show
the state on the card and a button that fixes it or opens the right page:

- `ms-settings:notifications` for notifications,
- `ms-settings:startupapps` for startup apps,
- `ms-settings:privacy-microphone` for the microphone.

Open them with `Launcher.LaunchUriAsync(new Uri("ms-settings:..."))`.
