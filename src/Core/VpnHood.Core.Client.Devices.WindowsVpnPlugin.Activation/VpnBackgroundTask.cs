using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using Windows.ApplicationModel.Background;
using Windows.Networking.Vpn;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin.Activation;

// What Windows activates in the VPN host when the profile connects: one finite run per VPN event.
public sealed class VpnBackgroundTask : IBackgroundTask
{
    public void Run(IBackgroundTaskInstance taskInstance)
    {
        var deferral = taskInstance.GetDeferral();
        try {
            var bridge = VpnPluginBridge.Current;

            // Despite its name, ProcessEventAsync is synchronous: it returns only once the plug-in's callback
            // (Connect, Disconnect...) has, so the deferral completes in the finally below, as Microsoft's
            // VPN sample does. A deferral held for the VPN's lifetime ends in ExecutionTimeExceeded and
            // reinitializes the connection.
            VpnChannel.ProcessEventAsync(bridge, taskInstance.TriggerDetails);

            // Windows ends its connection once Disconnect returns, not once this run does: the session's close that
            // Disconnect started ends under this deferral
            bridge.WaitForDisconnect();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The VPN background task failed.");
        }
        finally {
            deferral.Complete();
        }
    }
}
