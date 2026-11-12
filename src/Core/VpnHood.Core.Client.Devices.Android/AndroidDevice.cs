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

/// <summary>
/// Represents an Android device as the VPN app sees it: the VPN service is the app's own VpnService, in a process of its
/// own in Release builds, which the app starts and reaches through a binder.
/// </summary>
public class AndroidDevice : IDevice
{
    /// <summary>
    /// The VPN service's Java name, as the app reaches it without referencing it (<c>AndroidVpnService</c>): the
    /// service's <c>[Service]</c> attribute pins it, as the generated default hashes the namespace and the assembly name,
    /// so it is the name the service shipped under.
    /// </summary>
    public const string VpnServiceJavaName = "crc64198439f88d18e1a7.AndroidVpnService";

    /// <summary>The suffix of the VPN service's process, in Release builds: <c>:vpnhood_process</c>.</summary>
    public const string VpnServiceProcessName = ":vpnhood_process";

    /// <summary>The action of the intent that connects the VPN service: <c>connect</c>.</summary>
    public const string VpnServiceActionConnect = "connect";

    private TaskCompletionSource<bool> _grantPermissionTaskSource = new();
    private const int RequestVpnPermissionId = 20100;
    private volatile bool _isWaitingForVpnPermission;
    private readonly AndroidInstalledApps _installedApps = new();

    /// <inheritdoc/>
    public bool IsBindProcessToVpnSupported => true;

    /// <inheritdoc/>
    public bool IsExcludeAppsSupported => true;

    /// <inheritdoc/>
    public bool IsIncludeAppsSupported => true;

    /// <inheritdoc/>
    public bool IsQuicSupported => AndroidQuicClient.IsSupported;

    /// <inheritdoc/>
    /// <remarks>Always-on is the system's here.</remarks>
    public bool IsAlwaysOnSupported => false;

    /// <inheritdoc/>
    /// <remarks>None: the kill switch is the system's, its "Block connections without VPN".</remarks>
    public IVpnKillSwitch? KillSwitch => null;

    /// <inheritdoc/>
    public string OsInfo { get; } = $"{Build.Manufacturer}: {Build.Model}, Android: {Build.VERSION.Release}";

    /// <inheritdoc/>
    /// <remarks>As Chrome says it: Android's release and the model, and "Mobile" on a phone.</remarks>
    public DeviceUserAgentInfo UserAgentInfo { get; } = new() {
        Platform = $"Linux; Android {Build.VERSION.Release}",
        Model = Build.Model,
        Browser = DeviceUserAgentInfo.GetChromeBrowser(isMobile: AndroidUtils.IsPhone())
    };

    /// <inheritdoc/>
    /// <remarks>It is <see cref="GetVpnServiceConfigFolder"/>'s.</remarks>
    public string VpnServiceConfigFolder => GetVpnServiceConfigFolder();

    /// <inheritdoc/>
    /// <remarks>It is <see cref="GetVpnServiceDataFolder"/>'s.</remarks>
    public string VpnServiceDataFolder => GetVpnServiceDataFolder();

    /// <inheritdoc/>
    public bool IsTv => AndroidUtils.IsTv();

    /// <inheritdoc/>
    public bool IsWaitingForVpnPermission => _isWaitingForVpnPermission;

    /// <summary>Creates the device, in any process but the VPN service's, as the app's or the tile's.</summary>
    /// <returns>The device.</returns>
    /// <exception cref="InvalidOperationException">
    /// This is the VPN service's process, Android gave this process no name, or it named no cache folder for the app.
    /// </exception>
    public static AndroidDevice Create()
    {
        if (IsVpnServiceProcess)
            throw new InvalidOperationException(
                "Cannot create AndroidDevice in the VPN service process.");

        return new AndroidDevice();
    }

    /// <summary>
    /// Gets the folder of the VPN service's own files: <c>vpn-service</c> in the app's no-backup folder, which Android
    /// never puts in a backup or a transfer.
    /// </summary>
    /// <returns>The folder's path.</returns>
    /// <exception cref="InvalidOperationException">Android names no no-backup folder for the app.</exception>
    public static string GetVpnServiceConfigFolder()
    {
        return Path.Combine(Application.Context.NoBackupFilesDir?.AbsolutePath ??
                            throw new InvalidOperationException("Android named no no-backup folder for the app."), "vpn-service");
    }

    /// <summary>
    /// Gets the folder of the person's proxies: <c>vpn-service</c> beside the app's files, which travel with a backup.
    /// </summary>
    /// <returns>The folder's path.</returns>
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

    /// <inheritdoc/>
    public Task RequestVpnService(IUiContext? uiContext, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // prepare vpn service
        var androidUiContext = (AndroidUiContext?)uiContext;
        return PrepareVpnService(androidUiContext?.ActivityEvent, timeout, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It starts the VPN service with the connect action, as a foreground service from Android 8. It asks no
    /// permission: <see cref="RequestVpnService"/> does, which a null capture skips, as it would end another app's VPN.
    /// </remarks>
    public Task StartVpnService(CancellationToken cancellationToken)
    {
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

    /// <inheritdoc/>
    /// <remarks>Android's own figures, from its activity manager.</remarks>
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

    /// <summary>Gets this process's name: Android's word for it from Android 9, else the kernel's copy of it.</summary>
    /// <exception cref="InvalidOperationException">Android gave this process no name.</exception>
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

    /// <summary>
    /// Gets a value indicating whether this process is the VPN service's, by its name (<see cref="VpnServiceProcessName"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">Android gave this process no name.</exception>
    public static bool IsVpnServiceProcess => CurrentProcessName.Contains(VpnServiceProcessName);

    /// <summary>
    /// Gets a value indicating whether this process is the app's own: the one named after the package.
    /// </summary>
    /// <remarks>
    /// Every other process — the VPN service's, the tile's, any a library declares — carries a suffix, so a head must not
    /// rename its application's process (<c>[Application(Process = ...)]</c>).
    /// </remarks>
    /// <exception cref="InvalidOperationException">The process, or the app's context, has no name.</exception>
    public static bool IsMainProcess => CurrentProcessName == (Application.Context.PackageName
        ?? throw new InvalidOperationException("The Android context has no package name."));

    /// <inheritdoc/>
    /// <remarks>It is a binder client, with a dormant binding to the VPN service.</remarks>
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

    /// <inheritdoc/>
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

    /// <summary>Does nothing: Android owns the VPN service's lifetime.</summary>
    public void Dispose()
    {
    }
}