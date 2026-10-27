using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin.Activation;

// What Windows activates in the VPN host when the app opens the package's app service, its control channel to the VPN
// service. A run lasts as long as its connection, and keeps the host running meanwhile, which Windows freezes between
// VPN events otherwise.
public sealed class VpnAppServiceTask : IBackgroundTask
{
    public void Run(IBackgroundTaskInstance taskInstance)
    {
        var deferral = taskInstance.GetDeferral();
        _ = Serve(taskInstance, deferral);
    }

    private static async Task Serve(IBackgroundTaskInstance taskInstance, BackgroundTaskDeferral deferral)
    {
        if (taskInstance.TriggerDetails is not AppServiceTriggerDetails details) {
            VhLogger.Instance.LogError("Windows ran the app service's task without a connection.");
            deferral.Complete();
            return;
        }

        // never disposed: Windows may raise Canceled as the run ends (SystemPolicy), and a timer-less source holds nothing
        var canceledCts = new CancellationTokenSource();
        void OnCanceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
        {
            VhLogger.Instance.LogInformation("Windows ended a connection to the VPN host. AppService: {AppService}, Reason: {Reason}",
                details.Name, reason);
            canceledCts.TryCancel();
        }

        taskInstance.Canceled += OnCanceled;
        try {
            VhLogger.Instance.LogInformation("A process connected to the VPN host. AppService: {AppService}, ProcessId: {ProcessId}",
                details.Name, Environment.ProcessId);
            await VpnPluginBridge.Current.ServeAppConnection(details.AppServiceConnection, canceledCts.Token).Vhc();
            VhLogger.Instance.LogInformation("A connection to the VPN host has closed. AppService: {AppService}", details.Name);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "A connection to the VPN host failed. AppService: {AppService}", details.Name);
        }
        finally {
            taskInstance.Canceled -= OnCanceled;
            deferral.Complete();
        }
    }
}
