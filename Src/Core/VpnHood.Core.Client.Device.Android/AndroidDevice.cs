using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.Device.Droid.ActivityEvents;
using VpnHood.Core.Client.Device.Droid.Utils;
using VpnHood.Core.Client.Device.Exceptions;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.Device.Droid;

public class AndroidDevice : IDevice
{
    private TaskCompletionSource<bool> _grantPermissionTaskSource = new();
    private const int RequestVpnPermissionId = 20100;

    public bool IsBindProcessToVpnSupported => true;
    public bool IsExcludeAppsSupported => true;
    public bool IsIncludeAppsSupported => true;
    public bool IsTcpProxySupported => true;
    public string OsInfo { get; } = $"{Build.Manufacturer}: {Build.Model}, Android: {Build.VERSION.Release}";
    public string VpnServiceConfigFolder => AndroidVpnService.VpnServiceConfigFolder;
    public bool IsTv => AndroidUtil.IsTv();

    public static AndroidDevice Create()
    {
        if (IsVpnServiceProcess)
            throw new InvalidOperationException(
                "Cannot create AndroidDevice in the VPN service process.");

        return new AndroidDevice();
    }

    public DeviceAppInfo[] InstalledApps {
        get {
            var deviceAppInfos = new List<DeviceAppInfo>();
            var packageManager = Application.Context.PackageManager ??
                                 throw new Exception("Could not acquire PackageManager!");

            // Get all apps
            var applications = packageManager.GetInstalledApplications(PackageInfoFlags.MetaData);
            var currentAppId = Application.Context.PackageName;

            foreach (var appInfo in applications) {
                if (appInfo.PackageName == currentAppId)
                    continue;

                if (!IsVisibleApp(packageManager, appInfo))
                    continue;

                // 4. Load metadata
                var appName = appInfo.LoadLabel(packageManager);
                if (string.IsNullOrWhiteSpace(appName) || appName == appInfo.PackageName)
                    continue;

                var icon = appInfo.LoadIcon(packageManager);
                if (icon is null)
                    continue;

                deviceAppInfos.Add(new DeviceAppInfo {
                    AppId = appInfo.PackageName!,
                    AppName = appName,
                    IconPng = icon.DrawableEncodeToBase64(100)
                });
            }

            return deviceAppInfos.OrderBy(a => a.AppName).ToArray();
        }
    }

    private static bool IsVisibleApp(PackageManager packageManager, ApplicationInfo appInfo)
    {
        var appId = appInfo.PackageName;

        if (!appInfo.Enabled || string.IsNullOrWhiteSpace(appId))
            return false;

        // Does it have a Launcher icon? (Most user apps)
        if (packageManager.GetLaunchIntentForPackage(appId) != null)
            return true;

        // Is it an updated system app? (e.g., Chrome, Maps, YouTube)
        if ((appInfo.Flags & ApplicationInfoFlags.UpdatedSystemApp) != 0)
            return true;

        // Is it a known "Core" tool like Android Auto?
        if (appId == "com.google.android.projection.gearhead")
            return true;

        // Is it a non-system app?
        if ((appInfo.Flags & ApplicationInfoFlags.System) == 0)
            return true;

        return false;
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
            activityEvent.Activity.StartActivityForResult(prepareIntent, RequestVpnPermissionId);
            await Task.WhenAny(_grantPermissionTaskSource.Task, Task.Delay(userIntentTimeout, cancellationToken))
                .Vhc();

            if (!_grantPermissionTaskSource.Task.IsCompletedSuccessfully)
                throw new TimeoutException("Could not grant VPN permission in the given time.");

            if (!await _grantPermissionTaskSource.Task)
                throw new UserCanceledException("VPN permission has been rejected.");
        }
        finally {
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
        var intent = new Intent(Application.Context, typeof(AndroidVpnService));
        intent.PutExtra("manual", true);
        var res = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? Application.Context.StartForegroundService(intent.SetAction("connect"))
            : Application.Context.StartService(intent.SetAction("connect"));

        if (res == null)
            throw new VpnServiceException("Could not start AndroidVpnService.");

        return Task.CompletedTask;
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

    private static string CurrentProcessName {
        get {
            //if (OperatingSystem.IsAndroidVersionAtLeast(28))
            //return Application.ProcessName ?? "";

            var activityManager = (ActivityManager)Application.Context.GetSystemService(Context.ActivityService)!;
            var pid = Process.MyPid();
            return activityManager
                .RunningAppProcesses?
                .SingleOrDefault(x => x.Pid == pid)?
                .ProcessName ?? "";
        }
    }

    public static bool IsVpnServiceProcess => CurrentProcessName.Contains(AndroidVpnService.ProcessName);

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