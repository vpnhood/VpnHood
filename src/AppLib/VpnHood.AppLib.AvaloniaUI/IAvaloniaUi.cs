namespace VpnHood.AppLib.AvaloniaUI;

// A UI a head can run. A head names one as a type argument - AvaloniaDesktopHost.Run<TUi>, the
// Android application's and the iOS delegate's - so the UI it does not name is not in the build,
// and porting a head to another UI is that one name and the package reference behind it.
//
// The members are static because a head must answer both questions before there is an instance:
// Avalonia makes the Application itself, after the app has been configured with the answers.
public interface IAvaloniaUi
{
    // The languages this UI has words for, declared to the app as it is configured (AppData.Configure)
    // - so the app's language list and its best-culture choice are made from the words that exist.
    static abstract IReadOnlyList<string> AvailableCultures { get; }

    // Whatever must be in place before the first view: the folder a UI reads its pictures from,
    // the fonts its styles name. The head calls it, so the moment is the head's - on Android the
    // folder is a copy this makes, which must not happen in Application.OnCreate.
    static abstract void PrepareContent();
}
