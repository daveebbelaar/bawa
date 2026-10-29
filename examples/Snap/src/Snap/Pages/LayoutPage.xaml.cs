using Snap.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Snap.Pages;

public sealed partial class LayoutPage : Page
{
    private static readonly string[] WideNames = ["Two thirds", "Three quarters", "Five sixths"];
    private readonly bool _loading = true;

    public LayoutPage()
    {
        InitializeComponent();
        for (var i = 0; i < SnapLayout.WideChoices.Length; i++)
            WideBox.Items.Add(new ComboBoxItem { Content = WideNames[i], Tag = SnapLayout.WideChoices[i] });
        var current = Array.FindIndex(SnapLayout.WideChoices, w => Math.Abs(w - App.Model.Wide) < 0.01);
        WideBox.SelectedIndex = Math.Max(0, current);
        GapSlider.Value = App.Model.Gap;
        GapText.Text = $"{App.Model.Gap} px";
        _loading = false;
    }

    private void OnWideChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && WideBox.SelectedItem is ComboBoxItem { Tag: double wide }) App.Model.Wide = wide;
    }

    private void OnGapChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (GapText is null) return;
        GapText.Text = $"{(int)e.NewValue} px";
        if (!_loading) App.Model.Gap = (int)e.NewValue;
    }
}
