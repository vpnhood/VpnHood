using VpnHood.Net.Toolkit.Extensions;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin;

// The process that keeps the VPN host awake while its VPN is connected, as the app's own connection does while the app
// runs: Windows freezes the host between VPN events unless a full-trust process holds one of its app services, and a
// reply arriving on the engine's sockets is no VPN event. It is the VPN host's own executable, which the host starts
// with full trust at a connect (the package's windows.fullTrustProcess extension), and it holds its app service until
// the host closes it with the session.
public static class WindowsVpnPluginKeepAwake
{
    // the package manifest's uap:AppService Name, and its desktop:ParameterGroup, whose Parameters are the argument,
    // in the VPN host's Application
    internal const string AppServiceName = "VpnKeepAwake";
    private const string ParameterGroupId = "KeepAwake";
    private const string HostApplicationId = "VpnPluginHost";
    public const string Argument = "-KeepAwake";

    // by the application's id: the background tasks' server is no current app to Windows (0x80070490)
    internal static Task Start() =>
        FullTrustProcessLauncher.LaunchFullTrustProcessForAppAsync(HostApplicationId, ParameterGroupId).AsTask();

    // the process's whole run
    public static async Task Run()
    {
        using var connection = new AppServiceConnection {
            AppServiceName = AppServiceName,
            PackageFamilyName = Package.Current.Id.FamilyName
        };

        var closedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ServiceClosed += (_, _) => closedTcs.TrySetResult();

        var status = await connection.OpenAsync().AsTask().Vhc();
        if (status != AppServiceConnectionStatus.Success)
            throw new InvalidOperationException($"Could not open the VPN host's keep-awake service. Status: {status}");

        await closedTcs.Task.Vhc();
    }
}
