namespace VpnHood.AppLib.Abstractions;

// The words the app draws where no UI can reach: a notification, a toast. Composed per
// language by AppResources; a caller that wants other words gives an IStringProvider, it does not
// write one here. Placeholders are numbered ("{0} access key has been added.") and composed by the
// caller.
public interface IAppStrings
{
    string Manage { get; }
    string MsgAccessKeyAdded { get; }
    string MsgAccessKeyUpdated { get; }
    string MsgCantReadAccessKey { get; }
    string MsgUnsupportedContent { get; }
    string UpdateReadyToInstall { get; }
}
