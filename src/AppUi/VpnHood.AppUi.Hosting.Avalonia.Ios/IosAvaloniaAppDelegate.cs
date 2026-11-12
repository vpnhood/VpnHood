using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.iOS;
using Avalonia.Platform.Storage;
using Foundation;
using UIKit;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Ios;
using VpnHood.AppLib.Abstractions.UiContexts;

namespace VpnHood.AppUi.Hosting.Avalonia.Ios;

/// <summary>
/// Represents the Avalonia UI's application delegate on iOS: a head's app delegate derives from it, says how its app
/// starts (<see cref="CreateInitParams"/>), and carries only what the system reads, its <c>[Register]</c> name.
/// </summary>
/// <remarks>
/// The app starts when Avalonia asks for the app builder; the UI then gets the app's API in process, and its content
/// from the UI content the app's options name, in the shipped heads a zip in the app bundle.
/// </remarks>
/// <typeparam name="TUi">The UI's application.</typeparam>
public abstract class IosAvaloniaAppDelegate<TUi> : AvaloniaAppDelegate<TUi>
    where TUi : Application, IAvaloniaUi, new()
{
    // ReSharper disable once NotAccessedField.Local
    private NSObject? _foregroundObserver; // kept for the app's life, as the observation is

    // A file or a link the app is opened with - an access key's, as the head's Info.plist declares - goes to the UI,
    // which imports it. Avalonia raises it as the scene connects, after the UI has made its view, and later as one
    // comes to the running app. A web page's link (a universal link) is not a key.
    /// <summary>Initializes a new instance of the <see cref="IosAvaloniaAppDelegate{TUi}"/> class.</summary>
    /// <remarks>
    /// Where the UI is a <see cref="VpnHoodAvaloniaAppBase"/>, the first file the app is opened with, or a link other than
    /// http or https, goes to it to import as an access key.
    /// </remarks>
    protected IosAvaloniaAppDelegate()
    {
        ((IAvaloniaAppDelegate)this).Activated += OnActivated;
    }

    /// <summary>Creates the head's init params: the app's, and the two iOS facts the platform builds its device from.</summary>
    /// <returns>The init params.</returns>
    protected abstract IosInitParams CreateInitParams();

#if !TVOS // a TV does not turn
    /// <summary>
    /// Gets the orientations the app allows a window, in place of the Info.plist's: portrait only on an iPhone, as the UI's
    /// wide layout is for an iPad, and all on an iPad.
    /// </summary>
    /// <param name="application">The app.</param>
    /// <param name="forWindow">The window, or <see langword="null"/>.</param>
    /// <returns>The orientations.</returns>
    [Export("application:supportedInterfaceOrientationsForWindow:")]
    public UIInterfaceOrientationMask GetSupportedInterfaceOrientations(UIApplication application, UIWindow? forWindow)
    {
        return UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Phone
            ? UIInterfaceOrientationMask.Portrait
            : UIInterfaceOrientationMask.All;
    }
#endif

    private static void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (Application.Current is not VpnHoodAvaloniaAppBase ui)
            return;

        switch (e) {
            case FileActivatedEventArgs { Files: [IStorageFile file, ..] }:
                _ = ui.ImportAccessKeyFile(file);
                break;

            // the link as it came: Uri.ToString unescapes
            case ProtocolActivatedEventArgs { Uri: { Scheme: not ("http" or "https") } uri }:
                _ = ui.ImportAccessKey(uri.OriginalString);
                break;
        }
    }

    /// <inheritdoc/>
    /// <remarks>It starts the app, then the UI over the app's API in process, and sets the UI context.</remarks>
    /// <exception cref="InvalidOperationException">
    /// The init params name no storage folder, or an App Group without a container; the platform has no local app data
    /// folder; the resolved storage folder has no parent; or the app's options name no UI content.
    /// </exception>
    protected override AppBuilder CreateAppBuilder()
    {
        VpnHoodIosApp.Init(CreateInitParams());
        AvaloniaUiHosting.StartAsync<TUi>(VpnHoodApp.Instance.Api, VpnHoodApp.Instance.UiAssetProvider,
            CancellationToken.None).GetAwaiter().GetResult();
        AppUiContext.Context = new IosUiContext();
        _foregroundObserver = UIApplication.Notifications.ObserveWillEnterForeground(
            (_, _) => AppUiContext.NotifyResumed());
        return base.CreateAppBuilder();
    }
}
