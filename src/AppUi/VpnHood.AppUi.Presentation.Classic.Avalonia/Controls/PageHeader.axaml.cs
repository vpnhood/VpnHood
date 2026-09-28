using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

public partial class PageHeader : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<PageHeader, string>(nameof(Title), "");

    // a glyph of the icon font (Mdi)
    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<PageHeader, string>(nameof(Icon), "");

    public PageHeader()
    {
        InitializeComponent();
        BackButton.IsVisible = !VhApp.IsTvUi;
    }

    public string Title {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Icon {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty && TitleBlock != null)
            TitleBlock.Text = Title;
        if (change.Property == IconProperty && IconBlock != null) {
            IconBlock.Text = Icon;
            IconBlock.IsVisible = Icon.Length > 0;
        }
    }

    // for a page that has nothing else to land on; on a TV, where there is no back button, such a
    // page takes no focus and the remote's Back leaves it
    public void FocusBack()
    {
        BackButton.LandFocus();
    }

    // the same pop the Back key does
    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        this.FindAncestorOfType<MainView>()?.GoBack();
    }
}
