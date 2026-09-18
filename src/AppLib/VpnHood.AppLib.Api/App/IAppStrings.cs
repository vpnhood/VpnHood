namespace VpnHood.AppLib.Api.App;

// The words the app draws where no UI can reach: a tray menu, a notification, a toast. Composed per
// language by AppResources; a caller that wants other words gives a provider, it does not write one
// here. Placeholders are numbered ("{0} access key has been added.") and composed by the caller.
public interface IAppStrings
{
    string Disconnect { get; }
    string Connect { get; }
    string Disconnected { get; }
    string Exit { get; }
    string Manage { get; }
    string MsgAccessKeyAdded { get; }
    string MsgAccessKeyUpdated { get; }
    string MsgCantReadAccessKey { get; }
    string MsgUnsupportedContent { get; }
    string Open { get; }
    string OpenInBrowser { get; }
}
