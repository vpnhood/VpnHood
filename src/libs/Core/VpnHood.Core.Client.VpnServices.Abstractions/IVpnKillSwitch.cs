namespace VpnHood.Core.Client.VpnServices.Abstractions;

/// <summary>
/// Defines the kill switch's block: the OS's own firewall rules, which let nothing out but the tunnel, the VPN service
/// itself and what <see cref="VpnKillSwitchRules"/> lets pass.
/// </summary>
/// <remarks>
/// It is the OS's, not the process's, so it stays through a crash of the service and, where the platform keeps it, a
/// reboot, until the app lifts it: at a person's disconnect, unless the app keeps it then (its lockdown), or when they
/// turn the switch off. Its presence is its whole state. Its rules' names are the SDK's, so every app built on VpnHood
/// shares one block, as it shares the tunnel (<c>IVpnTunnelLock</c>): the tunnel's holder puts its own up, or removes
/// whatever one a crashed or revoked holder left. Only the desktops have one, and each platform's has limits its class
/// names, as the traffic Windows' WinNAT carries, which no filter sees.
/// </remarks>
public interface IVpnKillSwitch
{
    /// <summary>Determines whether this app's block is up.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is <see langword="true"/> if this app's block is up; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="System.Runtime.InteropServices.ExternalException">
    /// The OS's rules could not be read: Linux's <c>nft</c>, or Windows' filtering engine.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<bool> IsBlocking(CancellationToken cancellationToken);

    /// <summary>
    /// Puts this app's block up, or re-points it, in one change with no gap, in place of any app's.
    /// </summary>
    /// <param name="rules">What the block lets pass.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the block is up.</returns>
    /// <remarks>It fails where it cannot, and then nothing may connect: a connect never goes on unprotected.</remarks>
    /// <exception cref="NotSupportedException">
    /// Linux has no nftables, or the service is in no cgroup v2 group of its own.
    /// </exception>
    /// <exception cref="System.Runtime.InteropServices.ExternalException">
    /// The OS refused the block: Linux's <c>nft</c>, or Windows' filtering engine, as without an administrator.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task Block(VpnKillSwitchRules rules, CancellationToken cancellationToken);

    /// <summary>
    /// Removes this app's block, or, with <paramref name="anyApp"/>, any app's: the tunnel's holder's call.
    /// </summary>
    /// <param name="anyApp">
    /// <see langword="true"/> to remove any app's block; <see langword="false"/> to remove this app's only.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the block is gone.</returns>
    /// <exception cref="System.Runtime.InteropServices.ExternalException">
    /// The OS refused the change: Linux's <c>nft</c>, or Windows' filtering engine.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task Unblock(bool anyApp, CancellationToken cancellationToken);
}
