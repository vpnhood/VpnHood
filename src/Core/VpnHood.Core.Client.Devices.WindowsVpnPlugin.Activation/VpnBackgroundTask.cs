using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.Core;
using Windows.Networking.Vpn;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin.Activation;

// What Windows activates in the VPN host when the profile connects: one finite run per VPN event.
[WinRT.WinRTRuntimeClassName(VpnActivationNames.GeneratedApplicationClass)]
public sealed class VpnBackgroundTask : IBackgroundTask
{
    // the one bridge of the process, kept across the task's runs
    private const string BridgeKey = "WindowsVpnPlugin.Bridge";

    public void Run(IBackgroundTaskInstance taskInstance)
    {
        var deferral = taskInstance.GetDeferral();
        try {
            var properties = (IDictionary<string, object>)CoreApplication.Properties;
            if (!properties.TryGetValue(BridgeKey, out var value) || value is not VpnPluginBridge bridge) {
                bridge = new VpnPluginBridge();
                properties[BridgeKey] = bridge;
            }

            // Despite its name, ProcessEventAsync is synchronous: it returns only once the plug-in's callback
            // (Connect, Disconnect...) has, so the deferral completes in the finally below, as Microsoft's
            // VPN sample does. A deferral held for the VPN's lifetime ends in ExecutionTimeExceeded and
            // reinitializes the connection.
            VpnChannel.ProcessEventAsync(bridge, taskInstance.TriggerDetails);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The VPN background task failed.");
        }
        finally {
            deferral.Complete();
        }
    }
}
