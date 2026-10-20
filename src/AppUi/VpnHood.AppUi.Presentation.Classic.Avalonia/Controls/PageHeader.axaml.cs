using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
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

    // none on a page that ends with its own way out (a prompt's OK), nor ever on a TV
    public bool IsBackVisible {
        get => BackButton.IsVisible;
        set => BackButton.IsVisible = value && !VhApp.IsTvUi;
    }

    // A picture page opens what it scrolls with a big title of its own, coloured words and all
    // (RichText). Both titles get the words, and the header shows its smaller one only once the
    // big one has scrolled out of sight under it, as the common apps' compact title bars do;
    // screen readers keep to the big one.
    public void SetLargeTitle(TextBlock largeTitle, string markup)
    {
        RichText.Apply(largeTitle, markup);
        RichText.Apply(TitleBlock, markup);
        TitleBlock.Opacity = 0;
        AutomationProperties.SetAccessibilityView(TitleBlock, AccessibilityView.Raw);

        var scroller = largeTitle.FindLogicalAncestorOfType<ScrollViewer>()
                       ?? throw new InvalidOperationException("A large title must be inside the page's scroller.");
        // out of sight: its bottom edge above the scroller's top
        scroller.ScrollChanged += (_, _) =>
            TitleBlock.Opacity = largeTitle.TranslatePoint(new Point(0, largeTitle.Bounds.Height), scroller) is { Y: <= 0 } ? 1 : 0;
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
