using System.Net;

namespace VpnHood.AppLib.App.WebHosting;

/// <summary>
/// Defines a place the app's own UI and API are served from (<see cref="IAppWebHostFactory"/>): the local one, on
/// loopback, which this device loads its UI from, or the remote one, on the LAN's addresses, which a phone pairs with.
/// </summary>
public interface IAppWebHost : IDisposable
{
    /// <summary>
    /// Starts the host where it is not, and returns the address to load, with the host bound and answering.
    /// </summary>
    /// <remarks>
    /// The first call binds; a later one is its caller's sign that it could not reach the host, after a failed load or
    /// a resume, and costs one real connect. The remote host also reads its addresses again, as a network can change
    /// under an open pairing screen. Nothing is bound until a caller asks.
    /// </remarks>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the address to load.</returns>
    /// <exception cref="InvalidOperationException">The host could not be bound on any address.</exception>
    /// <exception cref="ObjectDisposedException">The host is disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<Uri> EnsureStarted(CancellationToken cancellationToken);

    /// <summary>
    /// Lets the listeners go, until <see cref="EnsureStarted"/> binds them again; it does nothing while
    /// <see cref="IsAlwaysOn"/>. The app stops the remote host when the screen that opened it closes or a phone
    /// unpairs, and never stops the local one.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Stop(CancellationToken cancellationToken);

    /// <summary>
    /// Occurs when a listener was bound again under a caller that had loaded from it, so what it loaded may be half
    /// fetched: a page loaded from it reloads, and a phone tries again.
    /// </summary>
    event EventHandler? Restarted;

    /// <summary>
    /// Gets a value indicating whether the host is bound now, or held open for the life of the process.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets a value indicating whether the host is held open for the life of the process and asks for no pairing, as
    /// the remote host is in a Debug build or with the <see cref="Utils.DebugCommands.RemoteAccess"/> command; always
    /// <see langword="false"/> for the local host.
    /// </summary>
    bool IsAlwaysOn { get; }

    /// <summary>
    /// Gets the addresses to dial in at, the best guess first, each with whatever the listener asks for on the first
    /// request: none before the host starts, and none after it stops unless it is always on. They change when the host
    /// binds again.
    /// </summary>
    IReadOnlyList<Uri> Urls { get; }

    /// <summary>
    /// Gets the devices seen in the last few seconds, which tells presence, not sessions; always empty for the local
    /// host, which serves this device only.
    /// </summary>
    IReadOnlyList<IPAddress> ConnectedDevices { get; }
}
