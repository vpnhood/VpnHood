using VpnHood.AppLib.Api;
using VpnHood.AppUi.Common;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Avalonia;

// The Avalonia UI's start, written once for every host: the app's API handed to the UI, then what
// the UI needs before its first view - its content out of the stores the head named
// (AppOptions.UiZipAssets), the words and the fonts - and the app told which languages the UI has
// words for. A desktop window, an iOS scene and a browser page run it whole (StartAsync). Android
// runs it in its two halves, at the two moments its lifecycle gives: the API from the process's
// Application, before Avalonia initializes, since the features it reads decide the theme Avalonia
// applies as it does (InitAsync); the rest from the activity, before it makes the view
// (PrepareAsync). The host's lifetime and its UI context stay the host's.
//
// An in-process host waits for it on the thread that called, deliberately, rather than on a worker:
// on Android that is the activity's, where Avalonia is already up, and a UI that puts its fonts in
// place does it there. Blocking a thread that has a synchronization context is safe here because
// everything it waits for configures away from it (TaskExtensions.Vhc) - the rule this repo's async
// code already follows, and the one to keep when adding to this path. In process every step
// completes at once.
public static class AvaloniaUiHosting
{
    public static async Task StartAsync<TUi>(VpnHoodApi api, IAssetProvider? uiAssetProvider,
        CancellationToken cancellationToken)
        where TUi : IAvaloniaUi
    {
        await InitAsync(api, cancellationToken).Vhc();
        await PrepareAsync<TUi>(uiAssetProvider, cancellationToken).Vhc();
    }

    // The first half: the app's API, in process the app's own controllers, from a browser page the
    // same six interfaces over HTTP.
    public static Task InitAsync(VpnHoodApi api, CancellationToken cancellationToken)
    {
        return VhApp.Init(api, cancellationToken);
    }

    // The second half: the UI's content, and its languages told to the app.
    public static async Task PrepareAsync<TUi>(IAssetProvider? uiAssetProvider, CancellationToken cancellationToken)
        where TUi : IAvaloniaUi
    {
        var assets = uiAssetProvider ?? throw new InvalidOperationException(
            "The head has named no AppOptions.UiZipAssets, and this UI reads its pictures and its words from them.");

        await TUi.PrepareContentAsync(assets, cancellationToken).Vhc();
        await VhApp.Configure(TUi.AvailableCultures, cancellationToken).Vhc();
    }
}
