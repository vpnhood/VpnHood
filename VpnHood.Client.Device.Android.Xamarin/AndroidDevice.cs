// ReSharper disable once RedundantNullableDirective
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Net;
using Android.OS;
using static Android.Graphics.Bitmap;

namespace VpnHood.Client.Device.Droid
{
    public class AndroidDevice : IDevice
    {
        private readonly int _notificationId;
        private readonly Notification? _notification;
        private SemaphoreSlim _grantPermissionSemaphore = new(0);
        private SemaphoreSlim _startServiceSemaphore = new(0);
        private IPacketCapture? _packetCapture;
        private bool _permissionGranted;
        
        public static AndroidDevice? Current { get; private set; }

        public event EventHandler? OnStartAsService;

        public string OperatingSystemInfo => $"{Build.Manufacturer}: {Build.Model}, Android: {Build.VERSION.Release}";

        public AndroidDevice(Notification? notification = null, int notificationId = 3500)
        {
            if (Current != null)
                throw new InvalidOperationException($"Only one {nameof(AndroidDevice)} can be created!");

            _notification = notification;
            _notificationId = notificationId;
            Current = this;
        }

        private static Notification GetDefaultNotification()
        {
            const string channelId = "1000";
            var context = Application.Context;
            var notificationManager = (NotificationManager?)context.GetSystemService(Context.NotificationService)
                ?? throw new Exception("Could not resolve NotificationManager.");

            Notification.Builder notificationBuilder;
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                var channel = new NotificationChannel(channelId, "VPN",
                    NotificationImportance.Low);
                channel.EnableVibration(false);
                channel.EnableLights(false);
                channel.SetShowBadge(false);
                channel.LockscreenVisibility = NotificationVisibility.Public;
                channel.Importance = NotificationImportance.Low;
                notificationManager.CreateNotificationChannel(channel);
                notificationBuilder = new Notification.Builder(context, channelId);
            }
            else
            {
#pragma warning disable CS0618
                notificationBuilder = new Notification.Builder(context);
#pragma warning restore CS0618
            }

            var appInfo = Application.Context.ApplicationInfo ?? throw new Exception("Could not retrieve app info");
            return notificationBuilder
                .SetContentTitle("VpnHood!")
                .SetSmallIcon(appInfo.Icon)
                .SetOngoing(true)
                .Build();
        }

        public DeviceAppInfo[] InstalledApps
        {
            get
            {
                var deviceAppInfos = new List<DeviceAppInfo>();
                var packageManager = Application.Context.PackageManager ??
                                     throw new Exception("Could not acquire PackageManager!");
                var intent = new Intent(Intent.ActionMain);
                intent.AddCategory(Intent.CategoryLauncher);
                var resolveInfoList = packageManager.QueryIntentActivities(intent, 0);
                foreach (var resolveInfo in resolveInfoList)
                {
                    if (resolveInfo.ActivityInfo == null)
                        continue;

                    var appName = resolveInfo.LoadLabel(packageManager);
                    var appId = resolveInfo.ActivityInfo.PackageName;
                    var icon = resolveInfo.LoadIcon(packageManager);
                    if (appName is "" or null || appId is "" or null || icon == null)
                        continue;

                    var deviceAppInfo = new DeviceAppInfo(
                        appId,
                        appName,
                        EncodeToBase64(icon, 100)
                    );
                    deviceAppInfos.Add(deviceAppInfo);
                }

                return deviceAppInfos.ToArray();
            }
        }

        public bool IsExcludeAppsSupported => true;

        public bool IsIncludeAppsSupported => true;

        public async Task<IPacketCapture> CreatePacketCapture()
        {
            // Grant for permission if OnRequestVpnPermission is registered otherwise let service throw the error
            using var prepareIntent = VpnService.Prepare(Application.Context);
            if (OnRequestVpnPermission != null && prepareIntent != null)
            {
                _permissionGranted = false;
                OnRequestVpnPermission.Invoke(this, EventArgs.Empty);
                _grantPermissionSemaphore = new SemaphoreSlim(0);
                await _grantPermissionSemaphore.WaitAsync(10000);
                if (!_permissionGranted)
                    throw new Exception("Could not grant VPN permission in the given time.");
            }

            // start service
            var intent = new Intent(Application.Context, typeof(AndroidPacketCapture));
            intent.PutExtra("manual", true);
            Application.Context.StartForegroundService(intent.SetAction("connect"));

            // check is service started
            _startServiceSemaphore = new SemaphoreSlim(0);
            await _startServiceSemaphore.WaitAsync(10000);
            if (_packetCapture == null)
                throw new Exception("Could not start VpnService in the given time.");

            return _packetCapture;
        }

        public event EventHandler? OnRequestVpnPermission;

        private static string EncodeToBase64(Drawable drawable, int quality)
        {
            var bitmap = DrawableToBitmap(drawable);
            var stream = new MemoryStream();
            if (!bitmap.Compress(CompressFormat.Png, quality, stream))
                throw new Exception("Could not compress bitmap to png!");
            return Convert.ToBase64String(stream.ToArray());
        }

        private static Bitmap DrawableToBitmap(Drawable drawable)
        {
            if (drawable is BitmapDrawable { Bitmap: not null } drawable1)
                return drawable1.Bitmap;

            //var bitmap = CreateBitmap(drawable.IntrinsicWidth, drawable.IntrinsicHeight, Config.Argb8888);
            var bitmap = CreateBitmap(32, 32, Config.Argb8888!)!;
            var canvas = new Canvas(bitmap);
            drawable.SetBounds(0, 0, canvas.Width, canvas.Height);
            drawable.Draw(canvas);

            return bitmap;
        }

        public void VpnPermissionGranted()
        {
            _permissionGranted = true;
            _grantPermissionSemaphore.Release();
        }

        public void VpnPermissionRejected()
        {
            _grantPermissionSemaphore.Release();
        }

        internal void OnServiceStartCommand(AndroidPacketCapture packetCapture, Intent? intent)
        {
            _packetCapture = packetCapture;
            _startServiceSemaphore.Release();

            // set foreground
            var notification = _notification ?? GetDefaultNotification();
            packetCapture.StartForeground(_notificationId, notification);

            // fire AutoCreate for always on
            var manual = intent?.GetBooleanExtra("manual", false) ?? false;
            if (!manual)
                OnStartAsService?.Invoke(this, EventArgs.Empty);
        }
    }
}