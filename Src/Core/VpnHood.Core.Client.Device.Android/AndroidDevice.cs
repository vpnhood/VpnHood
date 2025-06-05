using Android.Content;
using Android.Content.Res;
using Android.Net;
using Android.OS;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.Device.Droid.ActivityEvents;
using VpnHood.Core.Client.Device.Droid.Utils;
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
    public bool IsAlwaysOnSupported { get; } = OperatingSystem.IsAndroidVersionAtLeast(24);
    public string OsInfo { get; } = $"{Build.Manufacturer}: {Build.Model}, Android: {Build.VERSION.Release}";
    public string VpnServiceConfigFolder => AndroidVpnService.VpnServiceConfigFolder;
    public bool IsTv { get; } = 
        ((UiModeManager?)Application.Context.GetSystemService(Context.UiModeService))?
        .CurrentModeType == UiMode.TypeTelevision;

    public static AndroidDevice Create()
    {
        return new AndroidDevice();
    }

    public DeviceAppInfo[] InstalledApps {
        get {
            var deviceAppInfos = new List<DeviceAppInfo>();
            var packageManager = Application.Context.PackageManager ??
                                 throw new Exception("Could not acquire PackageManager!");
            var intent = new Intent(Intent.ActionMain);
            intent.AddCategory(Intent.CategoryLauncher);
            var resolveInfoList = packageManager.QueryIntentActivities(intent, 0);

            var currentAppId = Application.Context.PackageName;
            foreach (var resolveInfo in resolveInfoList) {
                if (resolveInfo.ActivityInfo == null)
                    continue;

                var appName = resolveInfo.LoadLabel(packageManager);
                var appId = resolveInfo.ActivityInfo.PackageName;
                var icon = resolveInfo.LoadIcon(packageManager);
                if (appName is "" or null || appId is "" or null || icon == null || appId == currentAppId)
                    continue;

                var deviceAppInfo = new DeviceAppInfo {
                    AppId = appId,
                    AppName = appName,
                    IconPng = icon.DrawableEncodeToBase64(100)
                };
                deviceAppInfos.Add(deviceAppInfo);
            }

            return deviceAppInfos.ToArray();
        }
    }

    private async Task PrepareVpnService(IActivityEvent? activityEvent, TimeSpan userIntentTimeout, CancellationToken cancellationToken)
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
                .VhConfigureAwait();

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
        if (OperatingSystem.IsAndroidVersionAtLeast(26)) {
            Application.Context.StartForegroundService(intent.SetAction("connect"));
        }
        else {
            Application.Context.StartService(intent.SetAction("connect"));
        }

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

    private static IEnumerable<(Network, NetworkCapabilities)> GetNetworkWithCapabilities(ConnectivityManager connectivityManager)
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
        var connectivityManager = (ConnectivityManager?)Application.Context.GetSystemService(Context.ConnectivityService)!;

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