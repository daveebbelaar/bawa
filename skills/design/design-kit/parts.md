# Kit parts in WinUI 3

Code for each step of [SKILL.md](SKILL.md). It builds with C# 14, .NET 10,
and Windows App SDK 2.x (the `new-windows-app` template). Replace `<App>`
with the app's name. Rename `Kit` and `Ink` freely; keep the layers.

## Contents

- Wiring
- Tokens
- Type
- Part states
- Buttons
- Checkbox and text field
- Sidebar rows with keyboard navigation
- List rows
- Icons
- Title bar chrome
- Gallery

## Wiring

Files:

```text
src/<App>/DesignKit/
  Tokens.xaml          palette, meaning, scales, text styles
  Tokens.cs            the same scales for code, and Ink.Color
  Icons.cs             KitIcon and {kit:Icon}
  KitState.cs          forced states for the gallery
  KitGallery.xaml/.cs  every part in every state
  Parts/Buttons.xaml
  Parts/Fields.xaml
  Parts/Rows.xaml
```

`App.xaml`, after `XamlControlsResources` (later dictionaries win, which
is what lets the kit override WinUI's own keys):

```xml
<ResourceDictionary.MergedDictionaries>
    <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
    <ResourceDictionary Source="ms-appx:///UI/Space.xaml" />
    <ResourceDictionary Source="ms-appx:///DesignKit/Tokens.xaml" />
    <ResourceDictionary Source="ms-appx:///DesignKit/Parts/Buttons.xaml" />
    <ResourceDictionary Source="ms-appx:///DesignKit/Parts/Fields.xaml" />
    <ResourceDictionary Source="ms-appx:///DesignKit/Parts/Rows.xaml" />
</ResourceDictionary.MergedDictionaries>
```

Pages that use kit icons declare `xmlns:kit="using:<App>.DesignKit"`.

## Tokens

`DesignKit/Tokens.xaml`. Two rules came from testing this file:

- Inside a theme dictionary, read the palette with `{StaticResource}` and
  the system colors with `{ThemeResource}`. Never point at a key that
  lives in a theme dictionary (an `Ink` brush, a WinUI brush): the `Dark`
  dictionary got the `Light` value. So the WinUI overrides at the end of
  each theme dictionary repeat the palette values.
- Every key in one theme dictionary is in all three. Asked for a key it
  did not have, the `HighContrast` dictionary returned the `Light` value.

The theme dictionaries come first: the XAML compiler rejects keyed entries
both before and after them (WMC0035), and `{StaticResource}` still finds
the palette below.

```xml
<!--
  The kit's tokens in three layers. tools/grid-lint.ps1 skips this file.
  Layer 1, palette: raw colors by hue and step. Only this file reads them.
  Layer 2, meaning: Ink* brushes with a Light, a Dark, and a HighContrast value. Parts read only these.
  Layer 3, scales: Kit* sizes, radii, opacities, motion, and text styles.
-->
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Layer 2: meaning. -->
    <ResourceDictionary.ThemeDictionaries>
        <ResourceDictionary x:Key="Light">
            <SolidColorBrush x:Key="InkTextBrush" Color="{StaticResource PaletteGray900}" />
            <SolidColorBrush x:Key="InkTextSecondaryBrush" Opacity="0.62" Color="{StaticResource PaletteGray900}" />
            <SolidColorBrush x:Key="InkSurfaceBrush" Color="{StaticResource PaletteWhite}" />
            <SolidColorBrush x:Key="InkSidebarBrush" Color="{StaticResource PaletteGray50}" />
            <SolidColorBrush x:Key="InkAccentBrush" Color="{StaticResource PaletteBlue600}" />
            <SolidColorBrush x:Key="InkOnAccentBrush" Color="{StaticResource PaletteWhite}" />
            <SolidColorBrush x:Key="InkSelectionBrush" Color="{StaticResource PaletteBlue100}" />
            <SolidColorBrush x:Key="InkSelectedTextBrush" Color="{StaticResource PaletteBlue600}" />
            <SolidColorBrush x:Key="InkHoverBrush" Opacity="0.05" Color="{StaticResource PaletteGray900}" />
            <SolidColorBrush x:Key="InkDividerBrush" Opacity="0.08" Color="{StaticResource PaletteGray900}" />
            <SolidColorBrush x:Key="InkFocusBrush" Color="{StaticResource PaletteBlue600}" />
            <!-- WinUI's own controls follow the kit: lightweight styling by resource key. -->
            <SolidColorBrush x:Key="CheckBoxCheckBackgroundFillChecked" Color="{StaticResource PaletteBlue600}" />
            <SolidColorBrush x:Key="CheckBoxCheckBackgroundFillCheckedPointerOver" Color="{StaticResource PaletteBlue600}" />
            <SolidColorBrush x:Key="CheckBoxCheckBackgroundFillCheckedPressed" Color="{StaticResource PaletteBlue600}" />
            <SolidColorBrush x:Key="CheckBoxCheckBackgroundStrokeUnchecked" Opacity="0.62" Color="{StaticResource PaletteGray900}" />
            <SolidColorBrush x:Key="TextControlBorderBrushFocused" Color="{StaticResource PaletteBlue600}" />
        </ResourceDictionary>
        <ResourceDictionary x:Key="Dark">
            <SolidColorBrush x:Key="InkTextBrush" Color="{StaticResource PaletteSnow}" />
            <SolidColorBrush x:Key="InkTextSecondaryBrush" Opacity="0.62" Color="{StaticResource PaletteSnow}" />
            <SolidColorBrush x:Key="InkSurfaceBrush" Color="{StaticResource PaletteNight700}" />
            <SolidColorBrush x:Key="InkSidebarBrush" Color="{StaticResource PaletteNight}" />
            <SolidColorBrush x:Key="InkAccentBrush" Color="{StaticResource PaletteBlue400}" />
            <SolidColorBrush x:Key="InkOnAccentBrush" Color="{StaticResource PaletteNight}" />
            <SolidColorBrush x:Key="InkSelectionBrush" Color="{StaticResource PaletteBlue900}" />
            <SolidColorBrush x:Key="InkSelectedTextBrush" Color="{StaticResource PaletteBlue400}" />
            <SolidColorBrush x:Key="InkHoverBrush" Opacity="0.06" Color="{StaticResource PaletteWhite}" />
            <SolidColorBrush x:Key="InkDividerBrush" Opacity="0.08" Color="{StaticResource PaletteWhite}" />
            <SolidColorBrush x:Key="InkFocusBrush" Color="{StaticResource PaletteWhite}" />
            <SolidColorBrush x:Key="CheckBoxCheckBackgroundFillChecked" Color="{StaticResource PaletteBlue400}" />
            <SolidColorBrush x:Key="CheckBoxCheckBackgroundFillCheckedPointerOver" Color="{StaticResource PaletteBlue400}" />
            <SolidColorBrush x:Key="CheckBoxCheckBackgroundFillCheckedPressed" Color="{StaticResource PaletteBlue400}" />
            <SolidColorBrush x:Key="CheckBoxCheckBackgroundStrokeUnchecked" Opacity="0.62" Color="{StaticResource PaletteSnow}" />
            <SolidColorBrush x:Key="TextControlBorderBrushFocused" Color="{StaticResource PaletteBlue400}" />
        </ResourceDictionary>
        <!-- Contrast themes: the person's own colors, from Settings > Accessibility > Contrast themes. -->
        <ResourceDictionary x:Key="HighContrast">
            <SolidColorBrush x:Key="InkTextBrush" Color="{ThemeResource SystemColorWindowTextColor}" />
            <SolidColorBrush x:Key="InkTextSecondaryBrush" Color="{ThemeResource SystemColorWindowTextColor}" />
            <SolidColorBrush x:Key="InkSurfaceBrush" Color="{ThemeResource SystemColorWindowColor}" />
            <SolidColorBrush x:Key="InkSidebarBrush" Color="{ThemeResource SystemColorWindowColor}" />
            <SolidColorBrush x:Key="InkAccentBrush" Color="{ThemeResource SystemColorHighlightColor}" />
            <SolidColorBrush x:Key="InkOnAccentBrush" Color="{ThemeResource SystemColorHighlightTextColor}" />
            <SolidColorBrush x:Key="InkSelectionBrush" Color="{ThemeResource SystemColorHighlightColor}" />
            <SolidColorBrush x:Key="InkSelectedTextBrush" Color="{ThemeResource SystemColorHighlightTextColor}" />
            <SolidColorBrush x:Key="InkHoverBrush" Color="{ThemeResource SystemColorHighlightColor}" />
            <SolidColorBrush x:Key="InkDividerBrush" Color="{ThemeResource SystemColorWindowTextColor}" />
            <SolidColorBrush x:Key="InkFocusBrush" Color="{ThemeResource SystemColorWindowTextColor}" />
            <SolidColorBrush x:Key="CheckBoxCheckBackgroundFillChecked" Color="{ThemeResource SystemColorHighlightColor}" />
            <SolidColorBrush x:Key="CheckBoxCheckBackgroundFillCheckedPointerOver" Color="{ThemeResource SystemColorHighlightColor}" />
            <SolidColorBrush x:Key="CheckBoxCheckBackgroundFillCheckedPressed" Color="{ThemeResource SystemColorHighlightColor}" />
            <SolidColorBrush x:Key="CheckBoxCheckBackgroundStrokeUnchecked" Color="{ThemeResource SystemColorWindowTextColor}" />
            <SolidColorBrush x:Key="TextControlBorderBrushFocused" Color="{ThemeResource SystemColorHighlightColor}" />
        </ResourceDictionary>
    </ResourceDictionary.ThemeDictionaries>

    <!-- Layer 1: palette. -->
    <Color x:Key="PaletteWhite">#FFFFFFFF</Color>
    <Color x:Key="PaletteGray50">#FFF9FBFC</Color>
    <Color x:Key="PaletteGray900">#FF202020</Color>
    <Color x:Key="PaletteNight">#FF1E1F22</Color>
    <Color x:Key="PaletteNight700">#FF2A2B2F</Color>
    <Color x:Key="PaletteSnow">#FFF2F2F2</Color>
    <Color x:Key="PaletteBlue100">#FFDFEDFA</Color>
    <Color x:Key="PaletteBlue400">#FF4C95D3</Color>
    <Color x:Key="PaletteBlue600">#FF287ABC</Color>
    <Color x:Key="PaletteBlue900">#FF1D3A55</Color>

    <!-- Layer 3: scales. -->
    <x:Double x:Key="KitControlHeight">32</x:Double>
    <x:Double x:Key="KitRowHeight">36</x:Double>
    <x:Double x:Key="KitSidebarWidth">240</x:Double>
    <x:Double x:Key="KitIconSize">16</x:Double>
    <Thickness x:Key="KitButtonPadding">12,0,12,0</Thickness>
    <Thickness x:Key="KitRowPadding">12,0,12,0</Thickness>
    <Thickness x:Key="KitRowMargin">4,1,4,1</Thickness>
    <Thickness x:Key="KitFocusRingMargin">-3</Thickness>
    <Thickness x:Key="KitDividerInset">32,0,0,0</Thickness>
    <CornerRadius x:Key="KitRadius">6</CornerRadius>
    <CornerRadius x:Key="KitFocusRadius">9</CornerRadius>
    <CornerRadius x:Key="KitCheckRadius">10</CornerRadius>
    <x:Double x:Key="KitHoverOpacity">0.08</x:Double>
    <x:Double x:Key="KitPressedOpacity">0.14</x:Double>
    <x:Double x:Key="KitDisabledOpacity">0.4</x:Double>

    <!-- Motion: named intents. Strings, like WinUI's own: they convert to a Duration
         (VisualTransition, animations) but not to a TimeSpan (BrushTransition,
         ScalarTransition), which read them as zero. Those take KitMotion from Tokens.cs. -->
    <x:String x:Key="KitMotionHover">0:0:0.083</x:String>
    <x:String x:Key="KitMotionCheck">0:0:0.167</x:String>

    <!-- Type: see the next section. -->
</ResourceDictionary>
```

`DesignKit/Tokens.cs`, for code that needs a number or a color:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace <App>.DesignKit;

/// The kit's scales for code. The same values as Tokens.xaml, which XAML reads.
public static class KitMetric
{
    public const double ControlHeight = 32, RowHeight = 36, SidebarWidth = 240, IconSize = 16;
}

/// Motion for code: a BrushTransition or ScalarTransition takes a TimeSpan, which XAML strings cannot give.
public static class KitMotion
{
    public static readonly TimeSpan Hover = TimeSpan.FromMilliseconds(83);
    public static readonly TimeSpan Check = TimeSpan.FromMilliseconds(167);
}

/// A meaning brush's color for one theme, for code that cannot use {ThemeResource}: the caption buttons.
public static class Ink
{
    public static Windows.UI.Color Color(string key, ElementTheme theme)
    {
        var tokens = Application.Current.Resources.MergedDictionaries
            .First(d => d.Source?.AbsolutePath.EndsWith("DesignKit/Tokens.xaml") == true);
        var dictionary = (ResourceDictionary)tokens.ThemeDictionaries[theme == ElementTheme.Dark ? "Dark" : "Light"];
        var brush = (SolidColorBrush)dictionary[key];
        var c = brush.Color;
        return Windows.UI.Color.FromArgb((byte)Math.Round(c.A * brush.Opacity), c.R, c.G, c.B);
    }
}
```

## Type

At the end of `Tokens.xaml`. Styles are named by role and based on
WinUI's type ramp (Caption 12, Body 14, Body Strong 14 semibold, Subtitle
20, Title 28), so they keep its line height and trimming. A label leaves
`Foreground` unset, so a selected row can color it.

```xml
<Style x:Key="KitTitleText" BasedOn="{StaticResource BodyTextBlockStyle}" TargetType="TextBlock">
    <Setter Property="FontSize" Value="24" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="Foreground" Value="{ThemeResource InkTextBrush}" />
</Style>
<Style x:Key="KitBodyText" BasedOn="{StaticResource BodyTextBlockStyle}" TargetType="TextBlock">
    <Setter Property="Foreground" Value="{ThemeResource InkTextBrush}" />
</Style>
<Style x:Key="KitLabelText" BasedOn="{StaticResource BodyTextBlockStyle}" TargetType="TextBlock">
    <Setter Property="TextWrapping" Value="NoWrap" />
    <Setter Property="TextTrimming" Value="CharacterEllipsis" />
</Style>
<Style x:Key="KitSectionText" BasedOn="{StaticResource BodyStrongTextBlockStyle}" TargetType="TextBlock">
    <Setter Property="Foreground" Value="{ThemeResource InkTextSecondaryBrush}" />
</Style>
<Style x:Key="KitMetaText" BasedOn="{StaticResource CaptionTextBlockStyle}" TargetType="TextBlock">
    <Setter Property="Foreground" Value="{ThemeResource InkTextSecondaryBrush}" />
</Style>
```

A bundled font: copy it to `Assets/Fonts/`, add
`<Content Include="Assets\Fonts\*.ttf" CopyToOutputDirectory="PreserveNewest" />`
to the csproj, and set
`<Setter Property="FontFamily" Value="ms-appx:///Assets/Fonts/Inter.ttf#Inter" />`.
The part after `#` is the family name inside the file, not the file name.

## Part states

A template names its states in a `VisualStateManager`. The control moves
between them: `Button`, `ToggleButton`, `CheckBox`, `RadioButton`, and
`ToggleSwitch` go to `Focused` on keyboard focus and to `PointerFocused`
on a click, so a ring drawn only in `Focused` shows for keyboard users
only, as Windows' own focus rectangle does.

The gallery forces a state with `VisualStateManager.GoToState`, so hover
and pressed show up in a snapshot. `DesignKit/KitState.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace <App>.DesignKit;

/// Forces a visual state for the gallery, so hover, pressed, and focused show up in a snapshot.
///   <Button Style="{StaticResource KitPrimaryButtonStyle}" kit:KitState.Forced="PointerOver" />
/// Disabled and selected are real: set IsEnabled="False" or select the item.
public static class KitState
{
    public static readonly DependencyProperty ForcedProperty =
        DependencyProperty.RegisterAttached("Forced", typeof(string), typeof(KitState), new PropertyMetadata(null, OnForcedChanged));

    public static string? GetForced(DependencyObject o) => (string?)o.GetValue(ForcedProperty);
    public static void SetForced(DependencyObject o, string? v) => o.SetValue(ForcedProperty, v);

    private static void OnForcedChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not Control control) return;
        // The control sets its own state when it loads and when the theme changes. Go after it.
        control.Loaded += (_, _) => Apply(control);
        control.ActualThemeChanged += (_, _) => Apply(control);
    }

    /// Returns false when the template has no state with that name: a typo, or a part without that state.
    public static bool Apply(Control control)
    {
        var states = GetForced(control)?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        var all = true;
        foreach (var state in states) all &= VisualStateManager.GoToState(control, state, false);
        return all;
    }
}
```

State names differ per control. `CheckBox` uses combined names
(`UncheckedPointerOver`, `CheckedPressed`). `TextBox` has `PointerOver`
and `Focused` in `CommonStates`. A `ListViewItem` draws its states inside
`ListViewItemPresenter` and cannot be forced: check its hover by hand
once.

## Buttons

One template for every look. Hover and press lay the button's own text
color over it at a low opacity, so primary, secondary, and ghost share
the template and only set `Background` and `Foreground`.

Animate state changes with `Storyboard` and `VisualTransition`, not with
`OpacityTransition` on an element the states change: in testing, that
crashed the app (0xC0000005) when `GoToState` ran on a button outside the
window, as the gallery and tests do.

`DesignKit/Parts/Buttons.xaml`:

```xml
<!-- Kit buttons: one template, three looks. Real Buttons: keyboard, focus, and Narrator work. -->
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Style x:Key="KitButtonStyle" TargetType="Button">
        <Setter Property="Background" Value="{ThemeResource InkAccentBrush}" />
        <Setter Property="Foreground" Value="{ThemeResource InkOnAccentBrush}" />
        <Setter Property="BorderBrush" Value="Transparent" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Padding" Value="{StaticResource KitButtonPadding}" />
        <Setter Property="MinHeight" Value="{StaticResource KitControlHeight}" />
        <Setter Property="CornerRadius" Value="{StaticResource KitRadius}" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="HorizontalContentAlignment" Value="Center" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <!-- The kit draws its own focus ring in FocusStates. -->
        <Setter Property="UseSystemFocusVisuals" Value="False" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Grid
                        x:Name="Root"
                        Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}"
                        CornerRadius="{TemplateBinding CornerRadius}">
                        <VisualStateManager.VisualStateGroups>
                            <VisualStateGroup x:Name="CommonStates">
                                <!-- Every change between these states eases over the hover duration. -->
                                <VisualStateGroup.Transitions>
                                    <VisualTransition GeneratedDuration="{StaticResource KitMotionHover}" />
                                </VisualStateGroup.Transitions>
                                <VisualState x:Name="Normal" />
                                <VisualState x:Name="PointerOver">
                                    <Storyboard>
                                        <DoubleAnimation Storyboard.TargetName="StateLayer" Storyboard.TargetProperty="Opacity" To="{StaticResource KitHoverOpacity}" Duration="0" />
                                    </Storyboard>
                                </VisualState>
                                <VisualState x:Name="Pressed">
                                    <Storyboard>
                                        <DoubleAnimation Storyboard.TargetName="StateLayer" Storyboard.TargetProperty="Opacity" To="{StaticResource KitPressedOpacity}" Duration="0" />
                                    </Storyboard>
                                </VisualState>
                                <VisualState x:Name="Disabled">
                                    <VisualState.Setters>
                                        <Setter Target="Root.Opacity" Value="{StaticResource KitDisabledOpacity}" />
                                    </VisualState.Setters>
                                </VisualState>
                            </VisualStateGroup>
                            <!-- Button goes to Focused for keyboard focus and PointerFocused for a click. -->
                            <VisualStateGroup x:Name="FocusStates">
                                <VisualState x:Name="Focused">
                                    <VisualState.Setters>
                                        <Setter Target="FocusRing.Opacity" Value="1" />
                                    </VisualState.Setters>
                                </VisualState>
                                <VisualState x:Name="Unfocused" />
                                <VisualState x:Name="PointerFocused" />
                            </VisualStateGroup>
                        </VisualStateManager.VisualStateGroups>

                        <!-- Hover and press tint the button with its own text color, so one template fits every look. -->
                        <Border
                            x:Name="StateLayer"
                            Background="{TemplateBinding Foreground}"
                            CornerRadius="{TemplateBinding CornerRadius}"
                            Opacity="0" />
                        <ContentPresenter
                            x:Name="ContentPresenter"
                            Padding="{TemplateBinding Padding}"
                            HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}"
                            VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}"
                            AutomationProperties.AccessibilityView="Raw"
                            Content="{TemplateBinding Content}"
                            ContentTemplate="{TemplateBinding ContentTemplate}"
                            ContentTransitions="{TemplateBinding ContentTransitions}"
                            Foreground="{TemplateBinding Foreground}" />
                        <Border
                            x:Name="FocusRing"
                            Margin="{StaticResource KitFocusRingMargin}"
                            BorderBrush="{ThemeResource InkFocusBrush}"
                            BorderThickness="2"
                            CornerRadius="{StaticResource KitFocusRadius}"
                            IsHitTestVisible="False"
                            Opacity="0" />
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="KitPrimaryButtonStyle" BasedOn="{StaticResource KitButtonStyle}" TargetType="Button" />

    <Style x:Key="KitSecondaryButtonStyle" BasedOn="{StaticResource KitButtonStyle}" TargetType="Button">
        <Setter Property="Background" Value="{ThemeResource InkSelectionBrush}" />
        <Setter Property="Foreground" Value="{ThemeResource InkTextBrush}" />
    </Style>

    <Style x:Key="KitGhostButtonStyle" BasedOn="{StaticResource KitButtonStyle}" TargetType="Button">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Foreground" Value="{ThemeResource InkTextBrush}" />
        <Setter Property="FontWeight" Value="Normal" />
    </Style>
</ResourceDictionary>
```

Use it: `<Button Content="Add task" Style="{StaticResource KitPrimaryButtonStyle}" Click="OnAdd" />`.
The focus ring's radius is the button's radius plus the 3 epx it sits
outside: inner corners stay concentric.

## Checkbox and text field

Both stay WinUI's own controls. The colors come from the lightweight keys
in `Tokens.xaml`; the shape comes from a setter. The checkbox's box takes
its corner radius from the `CheckBox`, so a radius of half the box (20
epx) makes it round. `DesignKit/Parts/Fields.xaml`:

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- A round check: the box's corner radius follows the CheckBox's CornerRadius. -->
    <Style x:Key="KitCheckBoxStyle" BasedOn="{StaticResource DefaultCheckBoxStyle}" TargetType="CheckBox">
        <Setter Property="CornerRadius" Value="{StaticResource KitCheckRadius}" />
        <Setter Property="Foreground" Value="{ThemeResource InkTextBrush}" />
    </Style>

    <Style x:Key="KitTextBoxStyle" BasedOn="{StaticResource DefaultTextBoxStyle}" TargetType="TextBox">
        <Setter Property="CornerRadius" Value="{StaticResource KitRadius}" />
        <Setter Property="MinHeight" Value="{StaticResource KitControlHeight}" />
    </Style>
</ResourceDictionary>
```

To find a control's keys and states, read its default style in the WinUI
source: `<Control>_themeresources.xaml` under `CommonStyles` in the
`microsoft/microsoft-ui-xaml` repo.
`BasedOn="{StaticResource Default<Control>Style}"` keeps everything you do
not set.

## Sidebar rows with keyboard navigation

A custom sidebar is a `ListView`, never a stack of buttons: the list
gives arrow keys, selection, the focus rectangle, and Narrator's "1 of 4,
selected". The row's look lives in `ListViewItemPresenter`, which draws
every state from brushes. `DesignKit/Parts/Rows.xaml`:

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Style x:Key="KitSidebarItemStyle" BasedOn="{StaticResource DefaultListViewItemStyle}" TargetType="ListViewItem">
        <Setter Property="Foreground" Value="{ThemeResource InkTextBrush}" />
        <Setter Property="Padding" Value="{StaticResource KitRowPadding}" />
        <Setter Property="Margin" Value="{StaticResource KitRowMargin}" />
        <Setter Property="MinHeight" Value="{StaticResource KitRowHeight}" />
        <Setter Property="FocusVisualPrimaryBrush" Value="{ThemeResource InkFocusBrush}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ListViewItem">
                    <ListViewItemPresenter
                        x:Name="Root"
                        HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}"
                        VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}"
                        Control.IsTemplateFocusTarget="True"
                        ContentMargin="{TemplateBinding Padding}"
                        ContentTransitions="{TemplateBinding ContentTransitions}"
                        CornerRadius="{StaticResource KitRadius}"
                        DisabledOpacity="{StaticResource KitDisabledOpacity}"
                        FocusVisualMargin="{TemplateBinding FocusVisualMargin}"
                        FocusVisualPrimaryBrush="{TemplateBinding FocusVisualPrimaryBrush}"
                        FocusVisualPrimaryThickness="{TemplateBinding FocusVisualPrimaryThickness}"
                        FocusVisualSecondaryBrush="{TemplateBinding FocusVisualSecondaryBrush}"
                        FocusVisualSecondaryThickness="{TemplateBinding FocusVisualSecondaryThickness}"
                        PointerOverBackground="{ThemeResource InkHoverBrush}"
                        PointerOverForeground="{ThemeResource InkTextBrush}"
                        PressedBackground="{ThemeResource InkHoverBrush}"
                        SelectedBackground="{ThemeResource InkSelectionBrush}"
                        SelectedForeground="{ThemeResource InkSelectedTextBrush}"
                        SelectedPointerOverBackground="{ThemeResource InkSelectionBrush}"
                        SelectedPressedBackground="{ThemeResource InkSelectionBrush}"
                        SelectionCheckMarkVisualEnabled="False"
                        SelectionIndicatorVisualEnabled="False" />
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- List rows edge to edge; the row template draws its own divider.
         Use it on a ListView with SelectionMode="None" when the row's checkbox is the action. -->
    <Style x:Key="KitListRowStyle" BasedOn="{StaticResource DefaultListViewItemStyle}" TargetType="ListViewItem">
        <Setter Property="Padding" Value="0" />
        <Setter Property="Foreground" Value="{ThemeResource InkTextBrush}" />
    </Style>
</ResourceDictionary>
```

Use it in place of `NavigationView`:

```xml
<ListView
    x:Name="Sidebar"
    Width="{StaticResource KitSidebarWidth}"
    Background="{ThemeResource InkSidebarBrush}"
    ItemContainerStyle="{StaticResource KitSidebarItemStyle}"
    SelectionChanged="OnSidebarChanged"
    SelectionMode="Single" />
```

`SelectionIndicatorVisualEnabled="False"` drops Windows' 3 x 16 epx accent
pill, which this kit replaces with a fill. Keep it `True` (and set
`SelectionIndicatorBrush`) for a Windows-like sidebar. To keep
`NavigationView` and only recolor it, override its keys in `Tokens.xaml`
the same way (`NavigationViewItemBackgroundSelected` and the rest, from
its default style).

## List rows

Rows draw their own divider, inset to where the text starts. The template
lives in the page that uses it, because `{x:Bind}` needs the page's code:

```xml
<Page.Resources>
    <!-- A list row: the kit checkbox, and a divider inset to where the text starts. -->
    <DataTemplate x:Key="KitListRowTemplate" x:DataType="x:String">
        <Grid MinHeight="{StaticResource KitRowHeight}">
            <CheckBox Content="{x:Bind}" Style="{StaticResource KitCheckBoxStyle}" />
            <Border
                Height="1"
                Margin="{StaticResource KitDividerInset}"
                VerticalAlignment="Bottom"
                Background="{ThemeResource InkDividerBrush}" />
        </Grid>
    </DataTemplate>
</Page.Resources>
```

With real data, `x:DataType` is the model type (`core:Item`) and the
checkbox binds `IsChecked="{x:Bind IsDone, Mode=TwoWay}"`.

## Icons

Views name a `KitIcon`. The set maps each name to a Segoe Fluent Icons
glyph or to a drawn path, in one place. `DesignKit/Icons.cs`:

```csharp
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace <App>.DesignKit;

/// The kit's icon set. Views name a KitIcon, never a glyph code or a path, so the whole set
/// changes in one place.
public enum KitIcon { Inbox, Today, Search, Star, Add, Delete, Settings }

/// {kit:Icon Name=Inbox} in XAML: a new icon element for every use (an element has one parent).
///   <AppBarButton Icon="{kit:Icon Name=Add}" Label="New item" />
///   <NavigationViewItem Icon="{kit:Icon Name=Star, Filled=True}" Content="Starred" />
[MarkupExtensionReturnType(ReturnType = typeof(IconElement))]
public sealed partial class Icon : MarkupExtension
{
    public KitIcon Name { get; set; }
    public bool Filled { get; set; }

    protected override object ProvideValue() => KitIcons.Make(Name, Filled);
}

public static class KitIcons
{
    /// Segoe Fluent Icons code points: outline and filled. Look them up in the WinUI 3 Gallery
    /// (Iconography) or on Microsoft Learn ("Segoe Fluent Icons font").
    private static readonly Dictionary<KitIcon, (int Outline, int Filled)> Glyphs = new()
    {
        [KitIcon.Today] = (0xE787, 0xE787),      // Calendar
        [KitIcon.Search] = (0xE721, 0xE721),     // Search
        [KitIcon.Star] = (0xE734, 0xE735),       // FavoriteStar, FavoriteStarFill
        [KitIcon.Add] = (0xE710, 0xE710),        // Add
        [KitIcon.Delete] = (0xE74D, 0xE74D),     // Delete
        [KitIcon.Settings] = (0xE713, 0xE713),   // Settings
    };

    /// Drawn icons: a path on a 16 x 16 box, filled with the icon's Foreground. PathIcon fills,
    /// it never strokes, and it does not scale the path. F0 = even-odd fill (inner shapes cut
    /// holes); F1 = nonzero, the SVG default. This one is a tray: a frame and a shelf with a dip.
    private static readonly Dictionary<KitIcon, string> Paths = new()
    {
        [KitIcon.Inbox] = "F0 M4,2 L12,2 C13.1,2 14,2.9 14,4 L14,12 C14,13.1 13.1,14 12,14 L4,14 C2.9,14 2,13.1 2,12 L2,4 C2,2.9 2.9,2 4,2 Z " +
                          "M4,3 C3.45,3 3,3.45 3,4 L3,12 C3,12.55 3.45,13 4,13 L12,13 C12.55,13 13,12.55 13,12 L13,4 C13,3.45 12.55,3 12,3 Z " +
                          "M3,8 L6.5,8 L6.5,8.5 C6.5,9.3 7.2,10 8,10 C8.8,10 9.5,9.3 9.5,8.5 L9.5,8 L13,8 L13,9 L10.4,9 C10.2,10.1 9.2,11 8,11 C6.8,11 5.8,10.1 5.6,9 L3,9 Z",
    };

    public static IconElement Make(KitIcon icon, bool filled = false)
    {
        if (Paths.TryGetValue(icon, out var data))
            return new PathIcon { Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data) };
        var (outline, fill) = Glyphs[icon];
        return new FontIcon { Glyph = char.ConvertFromUtf32(filled ? fill : outline) };
    }
}
```

From an SVG to a `PathIcon`:

1. Draw on a 16 x 16 canvas, or scale the SVG to one.
2. Turn strokes into filled outlines in the editor (Inkscape: Path >
   Stroke to Path).
3. Copy the `d` attribute. Put `F1 ` in front for the SVG's default fill,
   or `F0 ` when the shape has holes drawn as inner paths.

An icon at another size goes in a `Viewbox`. A colored SVG icon (several
colors) is an `ImageIcon` with an `SvgImageSource` instead; it keeps its
own colors and does not follow the theme.

## Title bar chrome

The template already hides the system title bar and uses the `TitleBar`
control. Make it the kit's in `MainWindow.xaml`:

```xml
<TitleBar
    x:Name="AppTitleBar"
    Title="<App>"
    Background="{ThemeResource InkSidebarBrush}"
    IsBackButtonVisible="False"
    IsPaneToggleButtonVisible="True"
    PaneToggleRequested="OnPaneToggleRequested">
    <TitleBar.IconSource>
        <ImageIconSource ImageSource="ms-appx:///Assets/AppIcon.png" />
    </TitleBar.IconSource>
    <!-- Content sits in the middle; controls in it stay clickable, the rest drags the window. -->
    <TitleBar.Content>
        <AutoSuggestBox MaxWidth="480" PlaceholderText="Search" QueryIcon="Find" />
    </TitleBar.Content>
    <TitleBar.RightHeader>
        <Button
            AutomationProperties.Name="Settings"
            Content="{kit:Icon Name=Settings}"
            Style="{StaticResource KitGhostButtonStyle}"
            ToolTipService.ToolTip="Settings" />
    </TitleBar.RightHeader>
</TitleBar>
```

The `TitleBar` control works out the drag regions from its content: buttons,
text boxes, and combo boxes stay clickable, everything else drags. After
adding or removing title bar controls at run time, call
`AppTitleBar.RecomputeDragRegions()`.

The minimize, maximize, and close buttons belong to the system
(`AppWindow.TitleBar`). They take colors, not brushes, so repaint them
when the theme changes. In `MainWindow.xaml.cs`:

```csharp
// In the constructor, after ApplyTheme(...):
Root.ActualThemeChanged += (_, _) => PaintCaptionButtons();

// At the end of ApplyTheme(string mode):
PaintCaptionButtons();

/// The caption buttons in kit colors. The system draws them, so they take colors, not brushes.
private void PaintCaptionButtons()
{
    if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) return; // the system's contrast colors win
    var bar = AppWindow.TitleBar;
    var theme = Root.ActualTheme;
    bar.ButtonForegroundColor = DesignKit.Ink.Color("InkTextBrush", theme);
    bar.ButtonHoverForegroundColor = DesignKit.Ink.Color("InkTextBrush", theme);
    bar.ButtonHoverBackgroundColor = DesignKit.Ink.Color("InkSelectionBrush", theme);
    bar.ButtonInactiveForegroundColor = DesignKit.Ink.Color("InkTextSecondaryBrush", theme);
}
```

Leave the close button's red hover to the system. A solid kit surface
instead of Mica: set `Background="{ThemeResource InkSurfaceBrush}"` on
the root grid and remove `<MicaBackdrop />` from `MainWindow.xaml`.

## Gallery

Every part in every state, light and dark. The app shows it instead of
its pages when `APP_KIT_GALLERY=1` is set in a snapshot run.

`DesignKit/KitGallery.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="<App>.DesignKit.KitGallery"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="{ThemeResource InkSurfaceBrush}">
    <Page.Resources>
        <!-- The list row template from "List rows". -->
        <DataTemplate x:Key="KitListRowTemplate" x:DataType="x:String">
            <Grid MinHeight="{StaticResource KitRowHeight}">
                <CheckBox Content="{x:Bind}" Style="{StaticResource KitCheckBoxStyle}" />
                <Border
                    Height="1"
                    Margin="{StaticResource KitDividerInset}"
                    VerticalAlignment="Bottom"
                    Background="{ThemeResource InkDividerBrush}" />
            </Grid>
        </DataTemplate>
    </Page.Resources>
    <ScrollViewer>
        <StackPanel x:Name="Sections" Padding="{StaticResource PageMargin}" Spacing="{StaticResource SpaceXl}" />
    </ScrollViewer>
</Page>
```

`DesignKit/KitGallery.xaml.cs`:

```csharp
using <App>.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace <App>.DesignKit;

/// Every part in every state, like a storybook. Shown instead of the pages when APP_KIT_GALLERY=1,
/// so a snapshot run captures it in light and dark:
///   $env:APP_KIT_GALLERY = '1'; tools/snap.ps1; Remove-Item Env:APP_KIT_GALLERY
public sealed partial class KitGallery : Page
{
    public static bool Enabled => Environment.GetEnvironmentVariable("APP_KIT_GALLERY") == "1";

    // One column per state. Disabled is real (IsEnabled = false); the others are forced.
    private static readonly string[] Columns = ["rest", "hover", "pressed", "focused", "disabled"];

    public KitGallery()
    {
        InitializeComponent();

        var parts = Table();
        Row(parts, "Primary", () => new Button { Content = "Add task", Style = Kit("KitPrimaryButtonStyle") });
        Row(parts, "Secondary", () => new Button { Content = "Plan week", Style = Kit("KitSecondaryButtonStyle") });
        Row(parts, "Ghost", () => new Button { Content = "Cancel", Style = Kit("KitGhostButtonStyle") });
        // Stock controls keep the system focus visual, so their focused column stays empty.
        Row(parts, "Checkbox", () => new CheckBox { Content = "Done", Style = Kit("KitCheckBoxStyle") },
            state => state switch { "hover" => "UncheckedPointerOver", "pressed" => "UncheckedPressed", _ => null });
        Row(parts, "Checkbox on", () => new CheckBox { Content = "Done", IsChecked = true, Style = Kit("KitCheckBoxStyle") },
            state => state switch { "hover" => "CheckedPointerOver", "pressed" => "CheckedPressed", _ => null });
        Row(parts, "Field", () => new TextBox { PlaceholderText = "Task name", Width = 160, Style = Kit("KitTextBoxStyle") },
            state => state switch { "hover" => "PointerOver", "focused" => "Focused", _ => null });
        Section("Parts", parts);

        // Rows are real ListViews: the second row is selected, the last one disabled.
        var sidebar = new ListView { Width = KitMetric.SidebarWidth, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var name in new[] { "Inbox", "Today", "Upcoming", "Archive" })
            sidebar.Items.Add(new ListViewItem { Content = name, Style = Kit("KitSidebarItemStyle"), IsEnabled = name != "Archive" });
        sidebar.SelectedIndex = 1;
        Section("Sidebar rows", sidebar);

        var list = new ListView
        {
            Width = KitMetric.SidebarWidth * 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            SelectionMode = ListViewSelectionMode.None,
            ItemContainerStyle = Kit("KitListRowStyle"),
            ItemTemplate = (DataTemplate)Resources["KitListRowTemplate"],
            ItemsSource = new[] { "Write the launch post", "Book the dentist", "Plan the week" },
        };
        Section("List rows", list);

        var type = new StackPanel { Spacing = Space.Sm };
        foreach (var role in new[] { "KitTitleText", "KitBodyText", "KitLabelText", "KitSectionText", "KitMetaText" })
            type.Children.Add(new TextBlock { Text = role, Style = Kit(role) });
        Section("Type", type);

        var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.Lg };
        foreach (var icon in Enum.GetValues<KitIcon>())
        {
            icons.Children.Add(KitIcons.Make(icon));
            icons.Children.Add(KitIcons.Make(icon, filled: true));
        }
        Section("Icons", icons);
    }

    private static Style Kit(string key) => (Style)Application.Current.Resources[key];

    private static Grid Table()
    {
        var grid = new Grid { ColumnSpacing = Space.Xl, RowSpacing = Space.Lg };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c < Columns.Length; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Place(grid, new TextBlock { Text = Columns[c], Style = Kit("KitMetaText") }, 0, c + 1);
        }
        return grid;
    }

    /// One row: the part in every column. stateFor maps a column to a visual state name
    /// (null: no cell in that column). The default fits the kit's own templates.
    private static void Row(Grid grid, string name, Func<FrameworkElement> make, Func<string, string?>? stateFor = null)
    {
        stateFor ??= column => column switch { "hover" => "PointerOver", "pressed" => "Pressed", "focused" => "Focused", _ => null };
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(grid, new TextBlock { Text = name, Style = Kit("KitLabelText"), VerticalAlignment = VerticalAlignment.Center }, row, 0);
        for (var c = 0; c < Columns.Length; c++)
        {
            var part = make();
            if (Columns[c] == "disabled" && part is Control disabled) disabled.IsEnabled = false;
            else if (stateFor(Columns[c]) is { } state) KitState.SetForced(part, state);
            else if (Columns[c] != "rest") continue;
            Place(grid, part, row, c + 1);
        }
    }

    private void Section(string title, UIElement content)
    {
        Sections.Children.Add(new TextBlock { Text = title, Style = Kit("KitSectionText") });
        Sections.Children.Add(content);
    }

    private static void Place(Grid grid, FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }
}
```

Show it in a snapshot run. In `MainWindow.Show(string id)`, first:

```csharp
if (id == "kit")
{
    ContentFrame.Navigate(typeof(DesignKit.KitGallery));
    return;
}
```

In `Snapshot.RunIfRequestedAsync`, the page list:

```csharp
var pages = DesignKit.KitGallery.Enabled ? ["kit"] : Enum.GetValues<SidebarPage>().Select(p => p.Id()).Append("settings")
    .Where(p => only is null || only.Contains(p)).ToList();
```

Then `$env:APP_KIT_GALLERY = '1'; tools/snap.ps1; Remove-Item Env:APP_KIT_GALLERY`
writes `kit-light.png` and `kit-dark.png` to `%TEMP%\<App>-shots`. The
child app inherits the variable from the shell. A normal launch never
sets it, so people never see the gallery.
