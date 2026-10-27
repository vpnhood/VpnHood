using System.Net;
using VpnHood.AppUi.Hosting.Desktop.Platform;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsVpnPlugin;

// The far end of the window's loopback connection in the package's sandbox: the window's API is the app's, in this
// same process, and a sandboxed app (an AppContainer) reaches no loopback listener outside its package, while Windows'
// TCP table, which WindowsLoopbackPeerCheck reads, is closed to it - every call would be refused as "held by another
// program".
internal sealed class WindowsVpnPluginLoopbackPeerCheck : ILoopbackPeerCheck
{
    public Task<bool> IsOwnedBy(IPEndPoint local, IPEndPoint remote, int processId, CancellationToken cancellationToken)
    {
        return Task.FromResult(processId == Environment.ProcessId);
    }
}
