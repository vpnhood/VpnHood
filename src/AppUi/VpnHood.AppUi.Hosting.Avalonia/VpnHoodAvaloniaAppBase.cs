using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using VpnHood.AppUi.Common;

namespace VpnHood.AppUi.Hosting.Avalonia;

// The Avalonia Application a VpnHood UI derives from: one view, whatever hosts it. A single-view
// host (Android, tvOS, a browser) gets it as the main view; a desktop host gets it in a window
// that opens at the size of the layout it shows - a phone's, or a TV's panel when the device is a
// TV (/tv-mode) - as the web UI's Windows head does. The head gives the UI the app's API before
// this runs (VhApp.Init) and configures it before the view is made (VhApp.Configure); the views
// read nothing else.
public abstract class VpnHoodAvaloniaAppBase : Application
{
    // The TV layout gets the TV's panel: Android TV lays out at 960x540 dp (a 1920x1080 panel at
    // xhdpi), the measure the web UI's TV layout is judged at too; a logical px here is a dp. Any
    // other gets the phone-shaped window the web UI's Windows head opens (AppResources.WindowSize).
    // 360 wide is the narrow phone.
    protected virtual int WindowWidth => VhApp.IsTvUi ? 960 : 400;
    protected virtual int WindowHeight => VhApp.IsTvUi ? 540 : 700;
    protected virtual int MinWindowWidth => 360;
    protected virtual int MinWindowHeight => 480;

    // The UI itself, made once the head has configured it.
    protected abstract Control CreateMainView();

    public override void OnFrameworkInitializationCompleted()
    {
        // On Android the process's Application class - and so this initialization - is shared with
        // the VPN service and the quick tile, whose processes hold no app and are given no API:
        // they get no view.
        if (VhApp.IsInit)
            ShowMainView();
        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainView()
    {
        switch (ApplicationLifetime) {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new Window {
                    Title = VhApp.Features.AppName,
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
    // which languages it has (VhApp.Configure).
    private Control CreateConfiguredMainView()
    {
        if (!VhApp.IsConfigured)
            throw new InvalidOperationException(
                $"The UI has not been configured. A head must call {nameof(VhApp)}.{nameof(VhApp.Configure)} before the view is made.");

        return CreateMainView();
    }

    // the theme's brush for the one surface XAML does not reach: the desktop window itself. A UI
    // that names no such brush gets the platform's own window background.
    private IBrush? ThemeBrush(string key)
    {
        return this.TryFindResource(key, out var value) ? value as IBrush : null;
    }
}
