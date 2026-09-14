using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using VpnHood.AppLib.AvaloniaUI.Styles;
using VpnHood.AppLib.AvaloniaUI.Views;

namespace VpnHood.AppLib.AvaloniaUI;

// The Avalonia Application: one view, MainView, whatever hosts it. A single-view host (Android,
// tvOS) gets it as the main view; a desktop host gets it in a window that opens at a TV's size and
// can be dragged down to a phone's, so both layouts can be walked with the arrow keys on a PC.
// VpnHoodApp must be initialized by the host before this runs; the views bind to its instance.
public partial class VpnHoodAvaloniaApp : Application
{
    // Android TV lays out at 960x540 dp (a 1920x1080 panel at xhdpi), the measure the web UI's TV
    // layout is judged at too; a logical px here is a dp. 360 wide is the narrow phone.
    public const int PanelWidth = 960;
    public const int PanelHeight = 540;
    public const int MinPanelWidth = 360;
    public const int MinPanelHeight = 480;

    public override void Initialize()
    {
        // the product's palette before the styles that read it: VpnHoodApp is initialized by the
        // host first, except in the processes that get no view (below), which take the default.
        Resources.MergedDictionaries.Add(
            AppTheme.FromUiName(VpnHoodApp.IsInit ? VpnHoodApp.Instance.Features.UiName : null));
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // On Android the process's Application class - and so this initialization - is shared with
        // the VPN service and the quick tile, whose processes hold no VpnHoodApp: they get no view.
        if (VpnHoodApp.IsInit)
            ShowMainView();
        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainView()
    {
        var mainView = new MainView();
        switch (ApplicationLifetime) {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new Window {
                    Title = VpnHoodApp.Instance.Resources.Strings.AppName,
                    Width = PanelWidth,
                    Height = PanelHeight,
                    MinWidth = MinPanelWidth,
                    MinHeight = MinPanelHeight,
                    Background = ThemeBrush("AppBackgroundBrush"),
                    Content = mainView
                };
                break;

            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = mainView;
                break;
        }
    }

    // the theme's brush for the one surface XAML does not reach: the desktop window itself
    private IBrush ThemeBrush(string key)
    {
        return this.TryFindResource(key, out var value) && value is IBrush brush
            ? brush
            : throw new InvalidOperationException($"The theme has no brush named '{key}'.");
    }
}
