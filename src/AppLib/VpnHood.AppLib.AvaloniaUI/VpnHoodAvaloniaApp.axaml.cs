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
                    Background = new SolidColorBrush(AppTheme.Background),
                    Content = mainView
                };
                break;

            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = mainView;
                break;
        }
    }
}
