using Snap.Pages;
using Snap.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Snap;

/// The Settings window. Closing it hides it: Snap keeps running in the tray.
public sealed partial class MainWindow : Window
{
    private bool _closingForGood;

    public MainWindow(AppModel model)
    {
        Model = model;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        this.SizeAndCenter(960, 680);
        this.MinimumSize(640, 480);
        ApplyTheme(Prefs.Current.Theme);
        AppWindow.Closing += (_, args) =>
        {
            if (_closingForGood) return;
            args.Cancel = true;
            AppWindow.Hide();
        };

        foreach (var page in Enum.GetValues<SidebarPage>())
        {
            Nav.MenuItems.Add(new NavigationViewItem
            {
                Content = page.Title(),
                Icon = new FontIcon { Glyph = page.Glyph() },
                Tag = page.Id(),
            });
        }
        Show(model.Page.Id());
    }

    public AppModel Model { get; }

    /// Opens a page by id: a SidebarPage id.
    public void Show(string id)
    {
        var target = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == id);
        if (target is not null && !Equals(Nav.SelectedItem, target)) Nav.SelectedItem = target;
    }

    /// From the tray icon, the tray menu, or a second start of Snap.
    public void ShowSettings()
    {
        AppWindow.Show();
        Activate();
        Win32.ForceForeground(this.Handle());
    }

    public void CloseForGood()
    {
        _closingForGood = true;
        Close();
    }

    /// "system", "light", or "dark". Also sets the caption buttons to match.
    public void ApplyTheme(string mode)
    {
        Root.RequestedTheme = Theme.Element(mode);
        AppWindow.TitleBar.PreferredTheme = mode switch
        {
            "light" => TitleBarTheme.Light,
            "dark" => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    public void ReapplyAccent() => Theme.Reapply(Root);

    public FrameworkElement RootElement => Root;

    public void DrawGridOverlay()
    {
        if (GridOverlay.Enabled) GridOverlay.Draw(GridCanvas, Root);
    }

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string id } || SidebarPages.FromId(id) is not { } page) return;
        Model.Page = page;
        Title = $"{page.Title()} - Snap";
        var type = page switch
        {
            SidebarPage.Shortcuts => typeof(ShortcutsPage),
            SidebarPage.Layout => typeof(LayoutPage),
            _ => typeof(GeneralPage),
        };
        ContentFrame.Navigate(type, null, new SuppressNavigationTransitionInfo());
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => Nav.IsPaneOpen = !Nav.IsPaneOpen;
}
