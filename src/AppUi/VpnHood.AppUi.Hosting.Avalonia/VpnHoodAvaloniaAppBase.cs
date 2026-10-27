using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.Avalonia;

// The Avalonia Application a VpnHood UI derives from: one view, whatever hosts it. A single-view
// host (Android, tvOS, a browser) gets it as the main view; a desktop host gets it in a window that
// is never resized or maximized, in the shape the UI states (WindowFit), fitted to its screen
// (AvaloniaDesktopHost). The head gives the UI the app's API before this runs (VhApp.Init) and
// configures it before the view is made (VhApp.Configure); the views read nothing else.
public abstract class VpnHoodAvaloniaAppBase : Application
{
    // The UI itself, made once the head has configured it.
    protected abstract Control CreateMainView();

    // The colour of the OS chrome around the UI - a desktop window's title bar, Android's system bars -
    // the window background its theme's manifest names, the colour the app's launch showed; null where
    // the UI's store names none, and the chrome keeps the system's.
    public VhColor? ChromeColor => AvaloniaUiHosting.ThemeColors.WindowBackgroundColor;

    // The shape a desktop window takes for this UI (DesktopWindowFit): its layout's, which only its
    // design knows, so the UI states it and a head names the UI alone.
    public abstract DesktopWindowFitOptions WindowFit { get; }

    // An access key the OS opened the app with - its file, mailed on by an admin - which the UI imports as the
    // chosen server and says so, or that it could not read the key, in its own words: the key a desktop launch
    // read from its file (AvaloniaDesktopHost), or the file itself, as iOS hands it over. On the UI thread, once
    // the view is made; they fail only where none is.
    public abstract Task ImportAccessKey(string accessKey);
    public abstract Task ImportAccessKeyFile(IStorageFile file);

    // A Quick Launch - the Windows Store edition's entry of its own in Start (AvaloniaDesktopHost) - on which the UI
    // connects as its Connect button does, unless the app is connected or on its way. On the UI thread, once the view
    // is made.
    public abstract Task QuickLaunch();

    public override void OnFrameworkInitializationCompleted()
    {
        // Avalonia's designer makes the UI with no app and gives it no API: it gets no view.
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
                    CanResize = false,
                    CanMaximize = false,
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
