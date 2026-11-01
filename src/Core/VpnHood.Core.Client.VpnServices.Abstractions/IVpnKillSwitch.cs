namespace VpnHood.Core.Client.VpnServices.Abstractions;

// The kill switch's block: the OS's own firewall rules, which let nothing out but the tunnel, the VPN
// service itself and what VpnKillSwitchRules lets pass. It is the OS's, not the process's, so it stays
// through a crash of the service and a reboot, until a person disconnects or turns the switch off; its
// presence is its whole state. The names are the SDK's, so every app built on VpnHood shares one block, as
// it shares the tunnel (IVpnTunnelLock): the tunnel's holder puts its own up, or removes whatever one a
// crashed or revoked holder left. Only the desktops have one.
public interface IVpnKillSwitch
{
    // Whether this app's block is up.
    Task<bool> IsBlocking(CancellationToken cancellationToken);

    // This app's block, put up or re-pointed in one change with no gap, in place of any app's. Throws where
    // it cannot, and then nothing may connect: a connect never goes on unprotected.
    Task Block(VpnKillSwitchRules rules, CancellationToken cancellationToken);

    // Removes this app's block, or, with anyApp, any app's: the tunnel's holder's call.
    Task Unblock(bool anyApp, CancellationToken cancellationToken);
}
