using System.Net;

namespace VpnHood.AppUi.Hosting.Desktop.Platform;

/// <summary>
/// Defines how a process checks that the far end of a loopback connection it made belongs to the process it meant to
/// reach, as far as the platform can tell.
/// </summary>
/// <remarks>
/// The service frees a dead listener's port before its channel names the next, and anyone may take it. Windows reads
/// the owning process from its TCP table; Linux checks the owning user; the Windows plug-in, whose app runs in the
/// window's process, checks only that the process meant is this one.
/// </remarks>
public interface ILoopbackPeerCheck
{
    /// <summary>
    /// Checks whether the far end of a loopback connection belongs to a process, as far as the platform can tell.
    /// </summary>
    /// <param name="local">The connection's local end.</param>
    /// <param name="remote">The connection's remote end.</param>
    /// <param name="processId">The ID of the process meant to own the remote end.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is <see langword="true"/> where the platform's check passes.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<bool> IsOwnedBy(IPEndPoint local, IPEndPoint remote, int processId, CancellationToken cancellationToken);
}
