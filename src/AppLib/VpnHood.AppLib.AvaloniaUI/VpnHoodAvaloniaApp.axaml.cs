using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.AvaloniaUI.Styles;
using VpnHood.AppLib.AvaloniaUI.Views;

namespace VpnHood.AppLib.AvaloniaUI;

// The Avalonia Application: one view, MainView, whatever hosts it. A single-view host (Android,
// tvOS, a browser) gets it as the main view; a desktop host gets it in a window that opens at a
// TV's size and can be dragged down to a phone's, so both layouts can be walked with the arrow
// keys on a PC. The head gives the UI the app's API before this runs (AppData.Init) and
// configures it before the view is made (AppData.Configure); the views read nothing else.
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
        // below - where the head has made the folder ready by now (AppData.Configure). A desktop
        // host, iOS and a browser have; Android's Application starts Avalonia before its activity
        // configures the UI - and in processes that never get one - so there the activity
        // registers them, before its view. Asked, never resolved: the resolving is a copy on
        // Android, and its moment is the head's.
        if (AppContent.IsResolved)
            AppAssets.RegisterFonts();

        AvaloniaXamlLoader.Load(this);

        // The product's palette over the client's the XAML merged, before any style is applied: the
        // head hands the UI the app's API first, except in the processes that get no view (below),
        // which keep the default.
        var themeOverride = AppTheme.OverrideFor(AppData.IsInit ? AppData.Features.UiName : null);
        if (themeOverride != null)
            Resources.MergedDictionaries.Add(themeOverride);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // On Android the process's Application class - and so this initialization - is shared with
        // the VPN service and the quick tile, whose processes hold no app and are given no API:
        // they get no view.
        if (AppData.IsInit)
            ShowMainView();
        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainView()
    {
        switch (ApplicationLifetime) {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new Window {
                    Title = AppData.Features.AppName,
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

    // The view, once the head has configured the UI: the folder it draws from, and the app told
    // which languages it has (AppData.Configure).
    private static MainView CreateMainView()
    {
        if (!AppData.IsConfigured)
            throw new InvalidOperationException(
                $"The UI has not been configured. A head must call {nameof(AppData)}.{nameof(AppData.Configure)} before the view is made.");

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
