namespace VpnHood.Core.Client.Ads;

/// <summary>
/// Represents a session's wait for an ad, which it tries on one route after another
/// (<see cref="Abstractions.AdRoute"/>) until the app tells it the ad showed, or the last route failed. While a try goes
/// around the tunnel, the session sends its new traffic that way, but the DNS and pings it carries as packets.
/// </summary>
/// <remarks>Disposing it cancels a wait under way.</remarks>
public interface ISessionAdHandler : IDisposable
{
    internal event EventHandler? IsWaitingForChanged;
    internal bool IsWaitingForAd { get; }

    /// <summary>
    /// Starts a wait for a rewarded ad during the session, or joins the one under way: around the tunnel first, then
    /// through it where the app allows it (<see cref="Abstractions.ClientOptions.CanShowRewardedAdOverVpn"/>).
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the ad has been shown.</returns>
    /// <remarks>
    /// The connections of the moment stay as they go when a wait starts. Once a try around the tunnel ends, the session
    /// drops its TCP connections and forwarded ones, so the new ones follow the splits again; a try through the tunnel
    /// drops none.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled; the caller that started the wait this one joined canceled it,
    /// which ends it for every caller; or the handler or its session was disposed.
    /// </exception>
    /// <exception cref="Exception">The ad failed: the error <see cref="SetAdFailed"/> gave.</exception>
    Task WaitForAd(CancellationToken cancellationToken);

    /// <summary>Ends the wait for an ad: the ad has been shown.</summary>
    void SetAdOk();

    /// <summary>
    /// Sends a rewarded ad's data to the server, for its access manager to verify, and ends the wait for the ad with
    /// the outcome.
    /// </summary>
    /// <param name="rewardedAdData">The ad's data.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the server has taken the ad.</returns>
    /// <remarks>
    /// A failure fails the wait for the ad with the same error, unless the session's shutdown has canceled it already:
    /// no route after it tries the ad again, and a connect ends with it.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The session is disposed.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or the session was disposed meanwhile.
    /// </exception>
    /// <exception cref="Exception">The server answered the WebSocket upgrade otherwise than expected.</exception>
    /// <exception cref="TimeoutException">The server did not answer in time.</exception>
    /// <exception cref="VpnHood.Core.Common.Exceptions.SessionException">The server refused the ad or the session.</exception>
    /// <exception cref="VpnHood.Core.Common.Exceptions.MaintenanceException">The server is in maintenance.</exception>
    /// <exception cref="UnauthorizedAccessException">The server refused the request's proof.</exception>
    Task SendRewardedAdData(string rewardedAdData, CancellationToken cancellationToken);

    /// <summary>
    /// Ends the ad's try on its route with a failure: the wait moves to its next route at once, where one is left and
    /// <paramref name="tryNextRoute"/> is <see langword="true"/>, or else ends with the failure.
    /// </summary>
    /// <param name="ex">The failure, which the wait throws where it ends.</param>
    /// <param name="tryNextRoute">
    /// <see langword="false"/> for a failure no route helps, as the app's refusal of the device's ad blocker: a connect
    /// ends with it, its routes after the VPN untried, though its ad is flexible.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="ex"/> is <see langword="null"/>.</exception>
    void SetAdFailed(Exception ex, bool tryNextRoute);
}
