using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Net;
using Android.OS;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Device.Droid.ActivityEvents;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;
using Environment = System.Environment;
using Path = System.IO.Path;

namespace VpnHood.Core.Client.Device.Droid;

public class AndroidDevice : Singleton<AndroidDevice>, IDevice
{
    private TaskCompletionSource<bool> _grantPermissionTaskSource = new();
    private const int RequestVpnPermissionId = 20100;

    public bool IsExcludeAppsSupported => true;
    public bool IsIncludeAppsSupported => true;
    public bool IsAlwaysOnSupported => OperatingSystem.IsAndroidVersionAtLeast(24);
    public string OsInfo => $"{Build.Manufacturer}: {Build.Model}, Android: {Build.VERSION.Release}";
    public string VpnServiceSharedFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vpn-service");

    private AndroidDevice()
    {
    }

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
                    IconPng = EncodeToBase64(icon, 100)
                };
                deviceAppInfos.Add(deviceAppInfo);
            }

            return deviceAppInfos.ToArray();
        }
    }

    private async Task PrepareVpnService(IActivityEvent? activityEvent, TimeSpan userIntentTimeout, CancellationToken cancellationToken)
    {
        // Grant for permission if OnRequestVpnPermission is registered otherwise let service throw the error
        VhLogger.Instance.LogTrace("Preparing VpnService...");
        using var prepareIntent = VpnService.Prepare(activityEvent?.Activity ?? Application.Context);
        if (prepareIntent == null)
            return; // already prepared

        if (activityEvent == null)
            throw new Exception("Please open the app and grant VPN permission to proceed.");

        _grantPermissionTaskSource = new TaskCompletionSource<bool>();
        activityEvent.ActivityResultEvent += Activity_OnActivityResult;
        try {
            VhLogger.Instance.LogTrace("Requesting user consent...");
            activityEvent.Activity.StartActivityForResult(prepareIntent, RequestVpnPermissionId);
            await Task.WhenAny(_grantPermissionTaskSource.Task, Task.Delay(userIntentTimeout, cancellationToken))
                .VhConfigureAwait();

            if (!_grantPermissionTaskSource.Task.IsCompletedSuccessfully)
                throw new Exception("Could not grant VPN permission in the given time.");

            if (!await _grantPermissionTaskSource.Task)
                throw new Exception("VPN permission has been rejected.");
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

    public async Task StartVpnService(CancellationToken cancellationToken)
    {
        // throw exception if not prepared
        await PrepareVpnService(null, TimeSpan.FromSeconds(0), cancellationToken);

        // start service
        var intent = new Intent(Application.Context, typeof(AndroidVpnAdapter));
        intent.PutExtra("manual", true);
        if (OperatingSystem.IsAndroidVersionAtLeast(26)) {
            Application.Context.StartForegroundService(intent.SetAction("connect"));
        }
        else {
            Application.Context.StartService(intent.SetAction("connect"));
        }
    }

    private static string EncodeToBase64(Drawable drawable, int quality)
    {
        var bitmap = DrawableToBitmap(drawable);
        var stream = new MemoryStream();
        if (!bitmap.Compress(Bitmap.CompressFormat.Png!, quality, stream))
            throw new Exception("Could not compress bitmap to png.");
        return Convert.ToBase64String(stream.ToArray());
    }

    private static Bitmap DrawableToBitmap(Drawable drawable)
    {
        if (drawable is BitmapDrawable { Bitmap: not null } drawable1)
            return drawable1.Bitmap;

        //var bitmap = CreateBitmap(drawable.IntrinsicWidth, drawable.IntrinsicHeight, Config.Argb8888);
        var bitmap = Bitmap.CreateBitmap(32, 32, Bitmap.Config.Argb8888!);
        var canvas = new Canvas(bitmap);
        drawable.SetBounds(0, 0, canvas.Width, canvas.Height);
        drawable.Draw(canvas);

        return bitmap;
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

    public void Dispose()
    {
        DisposeSingleton();
    }
}