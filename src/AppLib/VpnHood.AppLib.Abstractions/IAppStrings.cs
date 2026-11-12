namespace VpnHood.AppLib.Abstractions;

/// <summary>
/// Represents the words the app draws where no UI can reach, as a notification or a toast, in the app's language.
/// </summary>
/// <remarks>
/// The app library composes them per language; a caller that wants other words gives an <see cref="IStringProvider"/>
/// rather than one of these. A placeholder is numbered, as <c>{0}</c>, and the caller fills it.
/// </remarks>
public interface IAppStrings
{
    /// <summary>Gets the word of the notification's button that opens the app: "Manage".</summary>
    string Manage { get; }

    /// <summary>Gets the word of the notification's button that disconnects: "Disconnect".</summary>
    string Disconnect { get; }

    /// <summary>Gets the message for an access key added: "{0} access key has been added.", <c>{0}</c> its name.</summary>
    string MsgAccessKeyAdded { get; }

    /// <summary>Gets the message for an access key updated: "{0} access key has been updated.", <c>{0}</c> its name.</summary>
    string MsgAccessKeyUpdated { get; }

    /// <summary>Gets the message for an access key the app could not read: "Could not read the access key."</summary>
    string MsgCantReadAccessKey { get; }

    /// <summary>Gets the message for a file the app does not take: "Unsupported file type."</summary>
    string MsgUnsupportedContent { get; }

    /// <summary>Gets the message for an update that is ready: "The update is ready to install."</summary>
    string UpdateReadyToInstall { get; }
}
