using System.ComponentModel;
using System.Globalization;
using VpnHood.AppLib.Api.App;

namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

/// <summary>
/// Defines the words a desktop host shows around the UI, in the UI's language: its tray's menu and tooltip, the
/// connection on the taskbar button, and the message it shows where it has no app to give the UI.
/// </summary>
/// <remarks>
/// The UI owns its words, so the host shows the UI's rather than its own. <see cref="INotifyPropertyChanged.PropertyChanged"/>
/// occurs, on any thread, when the language changes, with no property's name: every member is read again.
/// </remarks>
public interface IDesktopStrings : INotifyPropertyChanged
{
    /// <summary>Gets the word for opening the window.</summary>
    string Open { get; }

    /// <summary>Gets the word for connecting.</summary>
    string Connect { get; }

    /// <summary>Gets the word for disconnecting.</summary>
    string Disconnect { get; }

    /// <summary>Gets the words for ending the UI, which disconnects first while connected.</summary>
    string ExitAndDisconnect { get; }

    /// <summary>Gets the words that say the kill switch is blocking the network while there is no connection.</summary>
    string KillSwitchBlockingMessage { get; }

    /// <summary>Gets the words that say only an administrator of this computer may use the app.</summary>
    string AdministratorsOnly { get; }

    /// <summary>Gets the word for closing a message.</summary>
    string Close { get; }

    /// <summary>Gets a value indicating whether the words run right to left, as in Arabic and Persian.</summary>
    bool IsRightToLeft { get; }

    /// <summary>Gets the words for a connection's state.</summary>
    /// <param name="connectionState">The state.</param>
    /// <returns>The words.</returns>
    string ConnectionStateText(AppConnectionState connectionState);

    /// <summary>
    /// Sets the language to show where the host has no app to take it from: the words fall back to the language alone,
    /// then to English.
    /// </summary>
    /// <param name="culture">The culture, as the device's.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task SetCultureAsync(CultureInfo culture, CancellationToken cancellationToken);
}
