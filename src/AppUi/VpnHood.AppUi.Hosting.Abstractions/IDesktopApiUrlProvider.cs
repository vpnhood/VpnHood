namespace VpnHood.AppUi.Hosting.Abstractions;

// The app's API URL now, where its web host serves the page too; a rebind moves it. A UI that loaded
// the page from Current loads it again on Changed, which may come from any thread.
public interface IDesktopApiUrlProvider
{
    Uri Current { get; }

    event EventHandler? Changed;
}
