using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.Networking.Connectivity;
using Windows.Networking.Vpn;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin;

// The process that keeps the VPN host awake while its VPN is connected, as the app's own connection does while the app
// runs: Windows freezes the host between VPN events unless a full-trust process holds one of its app services, and a
// reply arriving on the engine's sockets is no VPN event. It is the VPN host's own executable, which the host starts
// with full trust at a connect (the package's windows.fullTrustProcess extension), and it holds its app service until
// the host closes it with the session. It also dials the VPN again at the wake when a sleep ended it: Windows
// disconnects every VPN as the machine sleeps or hibernates and dials none at the wake, not even with the profile's own
// always on, and this process is the package's one that runs through the sleep, the app or not.
public static class WindowsVpnPluginKeepAwake
{
    // the package manifest's uap:AppService Name, and its desktop:ParameterGroup, whose Parameters are the argument,
    // in the VPN host's Application
    internal const string AppServiceName = "VpnKeepAwake";
    private const string ParameterGroupId = "KeepAwake";
    private const string HostApplicationId = "VpnPluginHost";
    private const string LogFileName = "keep-awake.log";
    public const string Argument = "-KeepAwake";

    // RAS's reasons for a connection's end that mean a sleep: hibernation, a suspend. A person's disconnect, in the app
    // or in Windows' quick settings, is 631.
    private static readonly int[] SleepEndReasons = [832, 833];
    private static readonly TimeSpan DialRetryPause = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DialRetryTime = TimeSpan.FromMinutes(2);

    // by the application's id: the background tasks' server is no current app to Windows (0x80070490)
    internal static Task Start() =>
        FullTrustProcessLauncher.LaunchFullTrustProcessForAppAsync(HostApplicationId, ParameterGroupId).AsTask();

    // the process's whole run
    public static async Task Run()
    {
        var endsSince = DateTime.UtcNow; // RAS's ends from here on are this process's to dial again
        var sleepLog = new FileLogger(Path.Combine(WindowsVpnPluginFolders.VpnServiceFolder, LogFileName)) {
            AutoFlush = true
        };
        using var sleepWatch = new WindowsVpnPluginSleepWatch();
        using var connection = new AppServiceConnection {
            AppServiceName = AppServiceName,
            PackageFamilyName = Package.Current.Id.FamilyName
        };

        var closedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ServiceClosed += (_, _) => closedTcs.TrySetResult();

        var status = await connection.OpenAsync().AsTask().Vhc();
        if (status != AppServiceConnectionStatus.Success)
            throw new InvalidOperationException($"Could not open the VPN host's keep-awake service. Status: {status}");

        // the profile being connected, which RAS names at its connection's end, and which a dial finds again
        var profileNames = (await WindowsVpnPluginService.TryGetLiveProfiles().Vhc())?
            .Select(x => x.ProfileName).ToArray() ?? [];

        // Each wake dials the VPN again if the sleep ended it, as RAS's own word on the end tells, the host's connection
        // closed or not: the host closes it 15 s after its session, which a sleep may outlast, and a session the sleep
        // cut short ends only after the wake. The close asks RAS too, for a sleep Windows told of no suspend or wake,
        // and ends the run, though not before the wake of a sleep under way; a connection the host keeps for the dial's
        // connect keeps this process.
        while (true) {
            var wake = sleepWatch.NextWake;
            await Task.WhenAny(wake, closedTcs.Task).Vhc();
            if (!wake.IsCompleted && !sleepWatch.IsAwake)
                await wake.Vhc();

            if (FindEndReason(profileNames, endsSince) is { } reason && SleepEndReasons.Contains(reason)) {
                endsSince = DateTime.UtcNow;
                await DialAfterSleep(profileNames, reason, sleepLog).Vhc();
            }

            if (closedTcs.Task.IsCompleted)
                return;
        }
    }

    // Why RAS ended the profile's latest connection since a time, from its event 20226, whose strings are the
    // connection's id, the user, the profile's name and the reason; null when it has logged none.
    private static int? FindEndReason(string[] profileNames, DateTime since)
    {
        var query = new EventLogQuery("Application", PathType.LogName,
            "*[System[Provider[@Name='RasClient'] and EventID=20226]]") {
            ReverseDirection = true
        };

        using var reader = new EventLogReader(query);
        while (true) {
            using var record = reader.ReadEvent();
            if (record is null || record.TimeCreated?.ToUniversalTime() < since)
                return null;

            if (record.Properties.Count >= 4 &&
                profileNames.Contains(record.Properties[2].Value as string, StringComparer.OrdinalIgnoreCase))
                return int.TryParse(Convert.ToString(record.Properties[3].Value, CultureInfo.InvariantCulture),
                    out var reason) ? reason : null;
        }
    }

    // Once Windows sees the internet, unless someone has connected the VPN by then. Right after a wake Windows tells of
    // the internet before the network is back - 7 s on a VM, while it identified the network again and the server was
    // unreachable - so a dial that fails is made again every DialRetryPause for DialRetryTime; a server down longer is
    // the person's to see. Its log is the process's only one, so a run without a sleep writes none.
    private static async Task DialAfterSleep(string[] profileNames, int reason, FileLogger sleepLog)
    {
        if (!sleepLog.IsOpen) {
            sleepLog.Open(deleteOld: true);
            VhLogger.AddProvider(new TextLoggerProvider(sleepLog));
        }

        VhLogger.Instance.LogInformation(
            "A sleep has ended the VPN. Reason: {Reason}, Profiles: {Profiles}, Connectivity: {Connectivity}",
            reason, string.Join(", ", profileNames),
            NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel());

        try {
            await WaitForInternet().Vhc();
            var giveUpTime = DateTime.UtcNow + DialRetryTime;
            while (!await TryDial(profileNames).Vhc()) {
                if (DateTime.UtcNow >= giveUpTime) {
                    VhLogger.Instance.LogWarning("The VPN could not be dialed again after the sleep.");
                    return;
                }

                await Task.Delay(DialRetryPause).Vhc();
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not dial the VPN again after the sleep.");
        }
    }

    // Whether it is done: dialed, or no longer the sleep's to dial - up again, or gone.
    private static async Task<bool> TryDial(string[] profileNames)
    {
        var agent = new VpnManagementAgent();
        var packageFamilyName = Package.Current.Id.FamilyName;
        var profiles = await agent.GetProfilesAsync().AsTask().Vhc();
        var profile = profiles.OfType<VpnPlugInProfile>().FirstOrDefault(x =>
            profileNames.Contains(x.ProfileName, StringComparer.OrdinalIgnoreCase) &&
            string.Equals(x.VpnPluginPackageFamilyName, packageFamilyName, StringComparison.OrdinalIgnoreCase));
        if (profile is null) {
            VhLogger.Instance.LogWarning("Windows lists the VPN profile no more, so nothing is dialed.");
            return true;
        }

        if (profile.ConnectionStatus != VpnManagementConnectionStatus.Disconnected) {
            VhLogger.Instance.LogInformation("The VPN is not disconnected any more. Status: {Status}",
                profile.ConnectionStatus);
            return true;
        }

        VhLogger.Instance.LogInformation("Asking Windows to dial the VPN again...");
        var status = await agent.ConnectProfileAsync(profile).AsTask().Vhc();
        if (status != VpnManagementErrorStatus.Ok) {
            VhLogger.Instance.LogWarning("Windows could not dial the VPN. Status: {Status}", status);
            return false;
        }

        VhLogger.Instance.LogInformation("Windows has dialed the VPN again.");
        return true;
    }

    // Windows' word that the internet is reachable, without which a dial fails; after a wake it comes too soon, though.
    private static async Task WaitForInternet()
    {
        var internetTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNetworkStatusChanged(object sender)
        {
            if (IsOnInternet())
                internetTcs.TrySetResult();
        }

        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        try {
            if (IsOnInternet())
                return;

            VhLogger.Instance.LogInformation("Waiting for the internet...");
            await internetTcs.Task.Vhc();
        }
        finally {
            NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
        }
    }

    private static bool IsOnInternet() =>
        NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel() ==
        NetworkConnectivityLevel.InternetAccess;
}
