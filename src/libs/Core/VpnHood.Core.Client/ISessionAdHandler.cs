namespace VpnHood.Core.Client;

/// <summary>
/// Represents a session's wait for an ad: while it waits, the session sends its new traffic around the tunnel, but the
/// DNS and pings it carries as packets, so the ad loads through the device's own network, until the app tells it the
/// ad's outcome.
/// </summary>
/// <remarks>Disposing it cancels a wait under way.</remarks>
public interface ISessionAdHandler : IDisposable
{
    internal event EventHandler? IsWaitingForChanged;
    internal bool IsWaitingForAd { get; }
    internal Task<Exception?> TryWaitForAd(CancellationToken cancellationToken);

    /// <summary>Starts a wait for an ad, or joins the one under way, until the app tells the ad's outcome.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the ad has been shown.</returns>
    /// <remarks>
    /// The connections of the moment stay as they go when a wait starts. Once a wait it started ends, the session drops
    /// its TCP connections and forwarded ones, so the new ones follow the splits again.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or the handler or its session was disposed.
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
    /// A failure fails the wait for the ad with the same error, unless the session's shutdown has canceled it already.
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

    /// <summary>Ends the wait for an ad with its failure.</summary>
    /// <param name="ex">The failure, which the wait throws.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ex"/> is <see langword="null"/>.</exception>
    void SetAdFailed(Exception ex);
}