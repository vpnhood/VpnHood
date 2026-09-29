using System.Net;

namespace VpnHood.AppUi.Hosting.Cli.Abstractions;

// Whether the far end of a loopback connection this process made is the process it meant to reach:
// the service frees a dead listener's port before its channel names the next, and anyone may take it.
public interface ILoopbackPeerCheck
{
    Task<bool> IsOwnedBy(IPEndPoint local, IPEndPoint remote, int processId, CancellationToken cancellationToken);
}
