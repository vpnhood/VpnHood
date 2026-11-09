using Android.Content;
using Android.Net;
using Android.OS;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.Devices.Android.ActivityEvents;
using VpnHood.Core.Client.Devices.Android.InstalledApps;
using VpnHood.Core.Client.Devices.Android.Messaging;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Net.Quic.Android;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Client.Devices.Android;

public class AndroidDevice : IDevice
{
    // The VPN service as the app reaches it without referencing it (AndroidVpnService): its Java name, which the
    // service's [Service] attribute pins - the generated default hashes the namespace and the assembly name, so it is
    // the name the service shipped under - its process, in Release builds, and the action that connects it.
    public const string VpnServiceJavaName = "crc64198439f88d18e1a7.AndroidVpnService";
    public const string VpnServiceProcessName = ":vpnhood_process";
    public const string VpnServiceActionConnect = "connect";

    private TaskCompletionSource<bool> _grantPermissionTaskSource = new();
    private const int RequestVpnPermissionId = 20100;
    private volatile bool _isWaitingForVpnPermission;
    private readonly AndroidInstalledApps _installedApps = new();

    public bool IsBindProcessToVpnSupported => true;
    public bool IsExcludeAppsSupported => true;
    public bool IsIncludeAppsSupported => true;
    public bool IsQuicSupported => AndroidQuicClient.IsSupported;
    public bool IsAlwaysOnSupported => false; // the system's
    public IVpnKillSwitch? KillSwitch => null; // the system's, "Block connections without VPN"
    public string OsInfo { get; } = $"{Build.Manufacturer}: {Build.Model}, Android: {Build.VERSION.Release}";
    public DeviceUserAgentInfo UserAgentInfo { get; } = new() {
        Platform = $"Linux; Android {Build.VERSION.Release}",
        Model = Build.Model,
        Browser = DeviceUserAgentInfo.GetChromeBrowser(isMobile: AndroidUtils.IsPhone())
    };
    public string VpnServiceConfigFolder => GetVpnServiceConfigFolder();
    public string VpnServiceDataFolder => GetVpnServiceDataFolder();
    public bool IsTv => AndroidUtils.IsTv();
    public bool IsWaitingForVpnPermission => _isWaitingForVpnPermission;

    public static AndroidDevice Create()
    {
        if (IsVpnServiceProcess)
            throw new InvalidOperationException(
                "Cannot create AndroidDevice in the VPN service process.");

        return new AndroidDevice();
    }

    // the VPN service's own files: Android never puts the no-backup folder in a backup or a transfer
    public static string GetVpnServiceConfigFolder()
    {
        return Path.Combine(Application.Context.NoBackupFilesDir?.AbsolutePath ??
                            throw new InvalidOperationException("Android named no no-backup folder for the app."), "vpn-service");
    }

    // the person's proxies, beside the app's files, which travel with a backup
    public static string GetVpnServiceDataFolder()
    {
        return Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "vpn-service");
    }

    internal static ComponentName VpnServiceComponent => new(Application.Context, VpnServiceJavaName);

    internal static Intent CreateVpnServiceIntent(string action)
    {
        return new Intent(action).SetComponent(VpnServiceComponent);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DeviceAppInfo>> GetInstalledApps(CancellationToken cancellationToken)
    {
        return _installedApps.GetInstalledApps(cancellationToken);
    }

    private async Task PrepareVpnService(IActivityEvent? activityEvent, TimeSpan userIntentTimeout,
        CancellationToken cancellationToken)
    {
        // Grant for permission if OnRequestVpnPermission is registered otherwise let service throw the error
        VhLogger.Instance.LogDebug("Preparing VpnService...");
        using var prepareIntent = VpnService.Prepare(activityEvent?.Activity ?? Application.Context);
        if (prepareIntent == null)
            return; // already prepared

        if (activityEvent == null)
            throw new Exception("Please open the app and grant VPN permission to proceed.");

        _grantPermissionTaskSource = new TaskCompletionSource<bool>();
        activityEvent.ActivityResultEvent += Activity_OnActivityResult;
        try {
            VhLogger.Instance.LogDebug("Requesting user consent...");
            _isWaitingForVpnPermission = true;
            activityEvent.Activity.StartActivityForResult(prepareIntent, RequestVpnPermissionId);
            await Task.WhenAny(_grantPermissionTaskSource.Task, Task.Delay(userIntentTimeout, cancellationToken))
                .Vhc();

            if (!_grantPermissionTaskSource.Task.IsCompletedSuccessfully)
                throw new TimeoutException("Could not grant VPN permission in the given time.");

            if (!await _grantPermissionTaskSource.Task.Vhc())
                throw new UserCanceledException("VPN permission has been rejected.");
        }
        finally {
            _isWaitingForVpnPermission = false;
            activityEvent.ActivityResultEvent -= Activity_OnActivityResult;
        }
    }

    public Task RequestVpnService(IUiContext? uiContext, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // prepare vpn service
        var androidUiContext = (AndroidUiContext?)uiContext;
        return PrepareVpnService(androidUiContext?.ActivityEvent, timeout, cancellationToken);
    }

    public Task StartVpnService(CancellationToken cancellationToken)
    {
        // do not call RequestVpnService for NullCapture. It will close other VPN connections.
        // start service
        var intent = CreateVpnServiceIntent(VpnServiceActionConnect);
        intent.PutExtra("manual", true);
        var res = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? Application.Context.StartForegroundService(intent)
            : Application.Context.StartService(intent);

        return res != null 
            ? Task.CompletedTask 
            : throw new VpnServiceException("Could not start AndroidVpnService.");
    }

    private void Activity_OnActivityResult(object? sender, ActivityResultEventArgs e)
    {
        if (e.RequestCode == RequestVpnPermissionId)
            _grantPermissionTaskSource.TrySetResult(e.ResultCode == Result.Ok);
    }

    public DeviceMemInfo? MemInfo {
        get {
            var activityManager = (ActivityManager?)Application.Context.GetSystemService(Context.ActivityService);
            if (activityManager == null)
                return null;

            // Get memory info
            var memoryInfo = new ActivityManager.MemoryInfo();
            activityManager.GetMemoryInfo(memoryInfo);

            var totalMemory = memoryInfo.TotalMem; // Total memory in bytes
            var availableMemory = memoryInfo.AvailMem; // Available memory in bytes
            return new DeviceMemInfo {
                AvailableMemory = availableMemory,
                TotalMemory = totalMemory
            };
        }
    }

    public static string CurrentProcessName {
        get {
            // Both answers come from inside the process: Android 9 and later say it; the kernel's copy of
            // the same name - written before any Application code runs, then null padding - answers before
            // that and whenever Android does not. An unnamed process would read as the app's own, so it throws.
            if (OperatingSystem.IsAndroidVersionAtLeast(28) && Application.ProcessName is { Length: > 0 } processName)
                return processName;

            var name = File.ReadAllText("/proc/self/cmdline").Split('\0')[0];
            return name.Length > 0 ? name : throw new InvalidOperationException("Android gave this process no name.");
        }
    }

    public static bool IsVpnServiceProcess => CurrentProcessName.Contains(VpnServiceProcessName);

    // The app's own process is the one named after the package. Every other - the VPN service's, the
    // tile's, any a library declares - carries a suffix, so a head must not rename its application's
    // process ([Application(Process = ...)]).
    public static bool IsMainProcess => CurrentProcessName == (Application.Context.PackageName
        ?? throw new InvalidOperationException("The Android context has no package name."));

    public IMessageClient CreateMessageClient()
    {
        return new AndroidMessageClient();
    }

    private static IEnumerable<(Network, NetworkCapabilities)> GetNetworkWithCapabilities(
        ConnectivityManager connectivityManager)
    {
        var networks = connectivityManager.GetAllNetworks();
        foreach (var network in networks) {
            var capabilities = connectivityManager.GetNetworkCapabilities(network);
            if (capabilities != null &&
                capabilities.HasCapability(NetCapability.Internet) &&
                capabilities.HasCapability(NetCapability.Validated) &&
                capabilities.HasCapability(NetCapability.NotVpn))
                yield return (network, capabilities);
        }
    }

    public void BindProcessToVpn(bool value)
    {
        var connectivityManager =
            (ConnectivityManager?)Application.Context.GetSystemService(Context.ConnectivityService)!;

        // null is system default which is VPN if connected to VPN otherwise it is the default network
        if (value) {
            VhLogger.Instance.LogDebug("Binding process to the default network...");
            connectivityManager.BindProcessToNetwork(null);
            return;
        }

        VhLogger.Instance.LogDebug("Binding process to a non VPN network...");
        var netCaps = GetNetworkWithCapabilities(connectivityManager).ToArray();
        var network =
            netCaps.FirstOrDefault(x => x.Item2.HasTransport(TransportType.Ethernet)).Item1 ??
            netCaps.FirstOrDefault(x => x.Item2.HasTransport(TransportType.Wifi)).Item1 ??
            netCaps.FirstOrDefault(x => x.Item2.HasTransport(TransportType.Usb)).Item1 ??
            netCaps.FirstOrDefault(x => x.Item2.HasTransport(TransportType.Satellite)).Item1 ??
            netCaps.FirstOrDefault(x => x.Item2.HasTransport(TransportType.Bluetooth)).Item1 ??
            netCaps.FirstOrDefault(x => x.Item2.HasTransport(TransportType.Cellular)).Item1 ??
            throw new Exception("Could not find any non VPN network.");

        connectivityManager.BindProcessToNetwork(network);
    }

    public void Dispose()
    {
    }
}