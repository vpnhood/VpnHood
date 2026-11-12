using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.Avalonia;

/// <summary>Represents the Avalonia application a VpnHood UI derives from: one view, whatever hosts it.</summary>
/// <remarks>
/// A single-view host, as Android, tvOS or a browser, gets it as the main view; a desktop host gets it in a window that
/// cannot be resized or maximized, in the shape the UI states (<see cref="WindowFit"/>), fitted to its screen. The head
/// gives the UI the app's API before this runs, and configures it before the view is made; the views read nothing else.
/// </remarks>
public abstract class VpnHoodAvaloniaAppBase : Application
{
    /// <summary>Creates the UI's main view, once the head has configured the UI.</summary>
    /// <returns>The view.</returns>
    protected abstract Control CreateMainView();

    /// <summary>
    /// Gets the colour of the system's chrome around the UI, as a desktop window's title bar or Android's system bars: the
    /// window background its theme names; or <see langword="null"/> where the UI's content names none, and the host chooses
    /// the chrome's colour.
    /// </summary>
    public VhColor? ChromeColor => AvaloniaUiHosting.ThemeColors.WindowBackgroundColor;

    /// <summary>Gets the shape a desktop window takes for this UI: its layout's, which only its design knows.</summary>
    public abstract DesktopWindowFitOptions WindowFit { get; }

    /// <summary>
    /// Imports an access key the system opened the app with, as the chosen server, and says so, or that it could not read
    /// the key, in the UI's own words.
    /// </summary>
    /// <param name="accessKey">The access key, as a desktop launch read it from its file.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>It is called on the UI thread, once the view is made.</remarks>
    public abstract Task ImportAccessKey(string accessKey);

    /// <summary>
    /// Imports an access key's file the system opened the app with, as iOS hands it over, as <see cref="ImportAccessKey"/>
    /// does.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>It is called on the UI thread, once the view is made.</remarks>
    public abstract Task ImportAccessKeyFile(IStorageFile file);

    /// <summary>
    /// Connects as the UI's Connect button does, for a Quick Launch, as the Windows Store edition's own entry in Start,
    /// unless the app is connected or on its way.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>It is called on the UI thread, once the view is made.</remarks>
    public abstract Task QuickLaunch();

    /// <inheritdoc/>
    /// <remarks>
    /// It gives the host's lifetime the UI's view, as that lifetime takes one; Avalonia's designer, which makes the UI with
    /// no app, gets none.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The UI has the app's API but has not been configured, and the host's lifetime takes its view at once, as a desktop's
    /// or a single view's does.
    /// </exception>
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
                    Title = VhApp.Descriptor.AppName,
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

    /// <summary>
    /// Makes a new view in the desktop window, for what the views read only as they are made; a view in any other host
    /// stays as it is.
    /// </summary>
    /// <exception cref="InvalidOperationException">The head has not configured the UI.</exception>
    public void ReloadMainView()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
            window.Content = CreateConfiguredMainView();
    }

    // The view, once the head has configured the UI: the folder it draws from, and the app told
    // which languages it has (VhApp.Configure).
    private Control CreateConfiguredMainView()
    {
        if (!VhApp.IsConfigured)
            throw new InvalidOperationException(
                $"The UI has not been configured. A head must call {nameof(VhApp)}.{nameof(VhApp.Configure)} before the view is made.");

        var view = CreateMainView();
        return VhApp.IsTvUi ? new TvScreenFit { Child = view } : view;
    }

    // the theme's brush for the one surface XAML does not reach: the desktop window itself. A UI
    // that names no such brush gets the platform's own window background.
    private IBrush? ThemeBrush(string key)
    {
        return this.TryFindResource(key, out var value) ? value as IBrush : null;
    }
}
