using VpnHood.AppUi.Common;
using VpnHood.Core.Toolkit.Assets;

namespace VpnHood.AppUi.Hosting.Avalonia;

// A UI a head can run. A head names one as a type argument - AvaloniaDesktopHost.Run<TUi>, the
// Android application's and the iOS delegate's - so the UI it does not name is not in the build,
// and porting a head to another UI is that one name and the package reference behind it.
//
// The members are static because a head must answer both questions before there is an instance:
// Avalonia makes the Application itself, after the app has been configured with the answers.
public interface IAvaloniaUi
{
    // The languages this UI has words for, declared to the app as it is configured (VhApp.Configure)
    // - so the app's language list and its best-culture choice are made from the words that exist.
    static abstract IReadOnlyList<string> AvailableCultures { get; }

    // Whatever must be in place before the first view - the words, the fonts its styles name -
    // out of the store the head hands in: a folder in process, the app's web host from a browser
    // page. The head calls it, so the moment is the head's; asynchronous because a page cannot wait.
    static abstract Task PrepareContentAsync(IAssetProvider assets, CancellationToken cancellationToken);
}
