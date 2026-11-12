namespace VpnHood.AppLib.Api;

/// <summary>
/// Represents the app's API for what the UI asks of the device: its permissions, its settings pages, the store's
/// review and the share sheet.
/// </summary>
/// <remarks>
/// Each needs the app's window open: without one, a call fails with the app's <c>UiContextNotAvailableException</c>.
/// A failure comes as <see cref="IAppApi"/> describes, in process and over HTTP.
/// </remarks>
public interface IIntentsApi
{
    /// <summary>Asks the person to let the app show notifications.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is <see langword="true"/> where the app may show them now.</returns>
    /// <exception cref="NotSupportedException">The device cannot do it.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<bool> RequestNotification(CancellationToken cancellationToken);

    /// <summary>Asks the system to add the app's Quick Launch, which the person accepts or refuses.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is <see langword="true"/> where the Quick Launch is added, or was already.</returns>
    /// <exception cref="NotSupportedException">The device cannot do it.</exception>
    /// <exception cref="Exceptions.RequestQuickLaunchException">The system answered no at once.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<bool> RequestQuickLaunch(CancellationToken cancellationToken);

    /// <summary>Asks the person for a review of the app, as the store shows it.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">The store's review could not be asked for.</exception>
    /// <exception cref="NotSupportedException">The app has no store review.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task RequestUserReview(CancellationToken cancellationToken);

    /// <summary>Opens the system's VPN settings, where the person turns the system's kill switch on for the app.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="NotSupportedException">The device cannot do it.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task OpenKillSwitchSettings(CancellationToken cancellationToken);

    /// <summary>Opens the system's VPN settings, where the person turns always-on VPN on for the app.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="NotSupportedException">The device cannot do it.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task OpenAlwaysOnSettings(CancellationToken cancellationToken);

    /// <summary>Opens the system's settings.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="NotSupportedException">The device cannot do it.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task OpenSettings(CancellationToken cancellationToken);

    /// <summary>Opens the app's own page in the system's settings.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="NotSupportedException">The device cannot do it.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task OpenAppSettings(CancellationToken cancellationToken);

    /// <summary>Opens the app's notification settings in the system's settings.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="NotSupportedException">The device cannot do it.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task OpenAppNotificationSettings(CancellationToken cancellationToken);

    /// <summary>Hands the app's log, as <c>report.txt</c>, to an app the person picks in the device's share sheet.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">On iOS, there is no window to show the share sheet from.</exception>
    /// <exception cref="NotSupportedException">The device has no share sheet, as a TV.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task ShareLog(CancellationToken cancellationToken);
}