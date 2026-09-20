using VpnHood.Core.Toolkit.Assets;

namespace VpnHood.AppUi.Hosting.Avalonia;

// The step every in-process host takes between the app and its first view: the UI's content, out
// of the store the head named (AppOptions.UiZipAsset) - the words, the fonts. Waited for on the
// calling thread, beside the VhApp.Init and VhApp.Configure every host already waits for
// there, and for the same reason: every frame after it depends on it, and in process the reads
// complete at once.
//
// On the thread that called, deliberately, rather than on a worker: on Android that is the
// activity's, where Avalonia is already up, and a UI that puts its fonts in place does it there.
// Blocking a thread that has a synchronization context is safe here because everything it waits
// for configures away from it (TaskExtensions.Vhc) - the rule this repo's async code already
// follows, and the one to keep when adding to that path.
public static class AvaloniaUiHosting
{
    public static void PrepareContent<TUi>(IAssetProvider? uiAssetProvider) where TUi : IAvaloniaUi
    {
        var assets = uiAssetProvider ?? throw new InvalidOperationException(
            "The head has named no AppOptions.UiZipAsset, and this UI reads its pictures and its words from it.");

        TUi.PrepareContentAsync(assets, CancellationToken.None).GetAwaiter().GetResult();
    }
}
