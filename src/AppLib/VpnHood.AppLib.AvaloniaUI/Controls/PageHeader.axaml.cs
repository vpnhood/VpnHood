using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Views;

namespace VpnHood.AppLib.AvaloniaUI.Controls;

public partial class PageHeader : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<PageHeader, string>(nameof(Title), "");

    public PageHeader()
    {
        InitializeComponent();
    }

    public string Title {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty && TitleBlock != null)
            TitleBlock.Text = Title;
    }

    // for a page that has nothing else to land on
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
