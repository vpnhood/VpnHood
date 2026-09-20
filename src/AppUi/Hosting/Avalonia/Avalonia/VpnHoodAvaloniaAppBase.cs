using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using VpnHood.AppUi.Services;

namespace VpnHood.AppUi.Hosting.Avalonia;

// The Avalonia Application a VpnHood UI derives from: one view, whatever hosts it. A single-view
// host (Android, tvOS, a browser) gets it as the main view; a desktop host gets it in a window
// that opens at a TV's size and can be dragged down to a phone's, so both layouts can be walked
// with the arrow keys on a PC. The head gives the UI the app's API before this runs (AppModel.Init)
// and configures it before the view is made (AppModel.Configure); the views read nothing else.
public abstract class VpnHoodAvaloniaAppBase : Application
{
    // Android TV lays out at 960x540 dp (a 1920x1080 panel at xhdpi), the measure the web UI's TV
    // layout is judged at too; a logical px here is a dp. 360 wide is the narrow phone.
    protected virtual int WindowWidth => 960;
    protected virtual int WindowHeight => 540;
    protected virtual int MinWindowWidth => 360;
    protected virtual int MinWindowHeight => 480;

    // The UI itself, made once the head has configured it.
    protected abstract Control CreateMainView();

    public override void OnFrameworkInitializationCompleted()
    {
        // On Android the process's Application class - and so this initialization - is shared with
        // the VPN service and the quick tile, whose processes hold no app and are given no API:
        // they get no view.
        if (AppModel.IsInit)
            ShowMainView();
        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainView()
    {
        switch (ApplicationLifetime) {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new Window {
                    Title = AppModel.Features.AppName,
                    Width = WindowWidth,
                    Height = WindowHeight,
                    MinWidth = MinWindowWidth,
                    MinHeight = MinWindowHeight,
                    Background = ThemeBrush("BackgroundBrush"),
                    Content = CreateConfiguredMainView()
                };
                break;

            // Android, whose lifetime is both of these: the activity makes the view as it is
            // made, which is after it has named the assets folder the view draws from - this
            // runs earlier, from the process's Application - so it gets the factory, not a view
            case IActivityApplicationLifetime activity:
                activity.MainViewFactory = CreateConfiguredMainView;
                break;

            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = CreateConfiguredMainView();
                break;
        }
    }

    // The view, once the head has configured the UI: the folder it draws from, and the app told
    // which languages it has (AppModel.Configure).
    private Control CreateConfiguredMainView()
    {
        if (!AppModel.IsConfigured)
            throw new InvalidOperationException(
                $"The UI has not been configured. A head must call {nameof(AppModel)}.{nameof(AppModel.Configure)} before the view is made.");

        return CreateMainView();
    }

    // the theme's brush for the one surface XAML does not reach: the desktop window itself. A UI
    // that names no such brush gets the platform's own window background.
    private IBrush? ThemeBrush(string key)
    {
        return this.TryFindResource(key, out var value) ? value as IBrush : null;
    }
}
