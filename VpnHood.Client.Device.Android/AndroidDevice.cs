using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Net;
using Android.OS;
using Microsoft.Extensions.Logging;
using VpnHood.Client.Device.Droid.ActivityEvents;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Client.Device.Droid;

public class AndroidDevice : Singleton<AndroidDevice>, IDevice
{
    private TaskCompletionSource<bool> _grantPermissionTaskSource = new();
    private const int RequestVpnPermissionId = 20100;
    private AndroidDeviceNotification? _deviceNotification;

    public event EventHandler? StartedAsService;
    public bool IsExcludeAppsSupported => true;
    public bool IsIncludeAppsSupported => true;
    public bool IsAlwaysOnSupported => OperatingSystem.IsAndroidVersionAtLeast(24);
    public string OsInfo => $"{Build.Manufacturer}: {Build.Model}, Android: {Build.VERSION.Release}";

    private AndroidDevice()
    {

    }

    public static AndroidDevice Create()
    {
        return new AndroidDevice();
    }

    public void InitNotification(AndroidDeviceNotification deviceNotification)
    {
        _deviceNotification = deviceNotification;
    }

    private static AndroidDeviceNotification CreateDefaultNotification()
    {
        const string channelId = "1000";
        var context = Application.Context;
        var notificationManager = context.GetSystemService(Context.NotificationService) as NotificationManager
            ?? throw new Exception("Could not resolve NotificationManager.");

        Notification.Builder notificationBuilder;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(channelId, "VPN", NotificationImportance.Low);
            channel.EnableVibration(false);
            channel.EnableLights(false);
            channel.SetShowBadge(false);
            channel.LockscreenVisibility = NotificationVisibility.Public;
            notificationManager.CreateNotificationChannel(channel);
            notificationBuilder = new Notification.Builder(context, channelId);
        }
        else
        {
            notificationBuilder = new Notification.Builder(context);
        }

        // get default icon
        var appInfo = Application.Context.ApplicationInfo ?? throw new Exception("Could not retrieve app info");
        if (context.Resources == null) throw new Exception("Could not retrieve context.Resources.");
        var iconId = appInfo.Icon;
        if (iconId == 0) iconId = context.Resources.GetIdentifier("@mipmap/notification", "drawable", context.PackageName);
        if (iconId == 0) iconId = context.Resources.GetIdentifier("@mipmap/ic_launcher", "drawable", context.PackageName);
        if (iconId == 0) iconId = context.Resources.GetIdentifier("@mipmap/appicon", "drawable", context.PackageName);
        if (iconId == 0) throw new Exception("Could not retrieve default icon.");

        var notification = notificationBuilder
            .SetSmallIcon(iconId)
            .SetOngoing(true)
            .Build();

        return new AndroidDeviceNotification
        {
            Notification = notification,
            NotificationId = 3500
        };
    }

    public DeviceAppInfo[] InstalledApps
    {
        get
        {
            var deviceAppInfos = new List<DeviceAppInfo>();
            var packageManager = Application.Context.PackageManager ?? throw new Exception("Could not acquire PackageManager!");
            var intent = new Intent(Intent.ActionMain);
            intent.AddCategory(Intent.CategoryLauncher);
            var resolveInfoList = packageManager.QueryIntentActivities(intent, 0);

            var currentAppId = Application.Context.PackageName;
            foreach (var resolveInfo in resolveInfoList)
            {
                if (resolveInfo.ActivityInfo == null)
                    continue;

                var appName = resolveInfo.LoadLabel(packageManager);
                var appId = resolveInfo.ActivityInfo.PackageName;
                var icon = resolveInfo.LoadIcon(packageManager);
                if (appName is "" or null || appId is "" or null || icon == null || appId == currentAppId)
                    continue;

                var deviceAppInfo = new DeviceAppInfo
                {
                    AppId = appId,
                    AppName = appName,
                    IconPng = EncodeToBase64(icon, 100)
                };
                deviceAppInfos.Add(deviceAppInfo);
            }

            return deviceAppInfos.ToArray();
        }
    }

    private async Task PrepareVpnService(IActivityEvent? activityEvent)
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
        try
        {
            VhLogger.Instance.LogTrace("Requesting user consent...");
            activityEvent.Activity.StartActivityForResult(prepareIntent, RequestVpnPermissionId);
            await Task.WhenAny(_grantPermissionTaskSource.Task, Task.Delay(TimeSpan.FromMinutes(2)))
                .VhConfigureAwait();

            if (!_grantPermissionTaskSource.Task.IsCompletedSuccessfully)
                throw new Exception("Could not grant VPN permission in the given time.");

            if (!_grantPermissionTaskSource.Task.Result)
                throw new Exception("VPN permission has been rejected.");
        }
        finally
        {
            activityEvent.ActivityResultEvent -= Activity_OnActivityResult;
        }
    }

    public async Task<IPacketCapture> CreatePacketCapture(IUiContext? uiContext)
    {
        // prepare vpn service
        var androidUiContext = (AndroidUiContext?)uiContext;
        await PrepareVpnService(androidUiContext?.ActivityEvent);

        // start service
        var intent = new Intent(Application.Context, typeof(AndroidPacketCapture));
        intent.PutExtra("manual", true);
        AndroidPacketCapture.StartServiceTaskCompletionSource = new TaskCompletionSource<AndroidPacketCapture>();
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            Application.Context.StartForegroundService(intent.SetAction("connect"));
        }
        else
        {
            Application.Context.StartService(intent.SetAction("connect"));
        }

        // check is service started
        try
        {
            var packetCapture = await AndroidPacketCapture.StartServiceTaskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return packetCapture;
        }
        catch (Exception ex)
        {
            AndroidPacketCapture.StartServiceTaskCompletionSource.TrySetCanceled();
            throw new Exception("Could not create VpnService in given time.", ex);
        }
    }


    internal void OnServiceStartCommand(AndroidPacketCapture packetCapture, Intent? intent)
    {
        // set foreground
        _deviceNotification ??= CreateDefaultNotification();
        packetCapture.StartForeground(_deviceNotification.NotificationId, _deviceNotification.Notification);

        // fire AutoCreate for always on
        var manual = intent?.GetBooleanExtra("manual", false) ?? false;
        if (!manual)
        {
            try
            {
                StartedAsService?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Could not start service.");
            }
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

    public void Dispose()
    {
        _deviceNotification?.Notification.Dispose();
        DisposeSingleton();
    }
}
