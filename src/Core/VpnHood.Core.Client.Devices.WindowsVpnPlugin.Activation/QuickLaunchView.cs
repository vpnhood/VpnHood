using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.System;
using Windows.UI.Core;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin.Activation;

// A person's launch of the VPN host's entry in Start, Quick Launch, which the VPN host has for the Store's sake: the
// Store takes no application without an entry. It opens the app with its Quick Launch argument, by the app's own scheme
// (the package's manifest), and the app connects as its Connect button does - the app, not the VPN host, since only the
// app's connection keeps the host awake. A launch awaits a view, or Windows takes it for one that never started, and
// only a window in front may open another app. An exception ends the process, into Windows' Application log.
internal sealed partial class QuickLaunchView : IFrameworkViewSource, IFrameworkView
{
    // the app's protocol in the package's manifest, whose Parameters are the app's Quick Launch argument
    private const string AppScheme = "vh-quick-launch";

    public IFrameworkView CreateView() => this;

    public void Initialize(CoreApplicationView applicationView) => applicationView.Activated += OnActivated;
    public void SetWindow(CoreWindow window) { }
    public void Load(string entryPoint) { }
    public void Run() => CoreWindow.GetForCurrentThread().Dispatcher.ProcessEvents(CoreProcessEventsOption.ProcessUntilQuit);
    public void Uninitialize() { }

    // the scheme to this package alone, whatever else claims it
    private static async void OnActivated(CoreApplicationView sender, IActivatedEventArgs args)
    {
        CoreWindow.GetForCurrentThread().Activate();
        var options = new LauncherOptions { TargetApplicationPackageFamilyName = Package.Current.Id.FamilyName };
        if (!await Launcher.LaunchUriAsync(new Uri($"{AppScheme}:"), options))
            throw new InvalidOperationException("Windows did not open the app for Quick Launch.");

        CoreApplication.Exit();
    }
}
