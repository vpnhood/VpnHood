using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.AvaloniaUI.Styles;
using VpnHood.AppLib.AvaloniaUI.Views;

namespace VpnHood.AppLib.AvaloniaUI;

// The Avalonia Application: one view, MainView, whatever hosts it. A single-view host (Android,
// tvOS) gets it as the main view; a desktop host gets it in a window that opens at a TV's size and
// can be dragged down to a phone's, so both layouts can be walked with the arrow keys on a PC.
// VpnHoodApp must be initialized by the host before this runs; the views bind to its instance.
public class VpnHoodAvaloniaApp : Application
{
    // Android TV lays out at 960x540 dp (a 1920x1080 panel at xhdpi), the measure the web UI's TV
    // layout is judged at too; a logical px here is a dp. 360 wide is the narrow phone.
    public const int PanelWidth = 960;
    public const int PanelHeight = 540;
    public const int MinPanelWidth = 360;
    public const int MinPanelHeight = 480;

    public override void Initialize()
    {
        // The fonts of the assets folder, before the styles that name them are read with the XAML
        // below - where the head has named the folder by now. A desktop host and iOS have;
        // Android's Application starts Avalonia before its activity names it (and in processes
        // that never get an activity), so there the activity registers them, before its view.
        if (AppAssets.IsFolderPathSet)
            AppAssets.RegisterFonts();

        AvaloniaXamlLoader.Load(this);

        // The product's palette over the client's the XAML merged, before any style is applied:
        // VpnHoodApp is initialized by the host first, except in the processes that get no view
        // (below), which keep the default.
        var themeOverride = AppTheme.OverrideFor(VpnHoodApp.IsInit ? VpnHoodApp.Instance.Features.UiName : null);
        if (themeOverride != null)
            Resources.MergedDictionaries.Add(themeOverride);
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
        switch (ApplicationLifetime) {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new Window {
                    Title = VpnHoodApp.Instance.Resources.Strings.AppName,
                    Width = PanelWidth,
                    Height = PanelHeight,
                    MinWidth = MinPanelWidth,
                    MinHeight = MinPanelHeight,
                    Background = ThemeBrush("BackgroundBrush"),
                    Content = CreateMainView()
                };
                break;

            // Android, whose lifetime is both of these: the activity makes the view as it is
            // made, which is after it has named the assets folder the view draws from - this
            // runs earlier, from the process's Application - so it gets the factory, not a view
            case IActivityApplicationLifetime activity:
                activity.MainViewFactory = CreateMainView;
                break;

            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = CreateMainView();
                break;
        }
    }

    private static MainView CreateMainView()
    {
        // the languages this UI has, declared to the app as the web UI's configure call does
        var app = VpnHoodApp.Instance;
        app.Services.CultureProvider.AvailableCultures = [.. Strings.AvailableCultures];
        app.UpdateUi();
        return new MainView();
    }

    // the theme's brush for the one surface XAML does not reach: the desktop window itself
    private IBrush ThemeBrush(string key)
    {
        return this.TryFindResource(key, out var value) && value is IBrush brush
            ? brush
            : throw new InvalidOperationException($"The theme has no brush named '{key}'.");
    }
}
