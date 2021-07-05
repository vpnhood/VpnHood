using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using static Android.Graphics.Bitmap;

namespace VpnHood.Client.Device.Android
{
    public class AndroidDevice : IDevice
    {
        private readonly EventWaitHandle _serviceWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
        private readonly EventWaitHandle _grantPermisssionWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
        private IPacketCapture _packetCapture;
        private bool _permissionGranted = false;

        public event EventHandler OnStartAsService;
        public event EventHandler OnRequestVpnPermission;

        public string OperatingSystemInfo => $"{Build.Manufacturer}: {Build.Model}, Android: {Build.VERSION.Release}";
        public DeviceAppInfo[] InstalledApps
        {
            get
            {
                var deviceAppInfos = new List<DeviceAppInfo>();

                var packageManager = Application.Context.PackageManager;
                var intent = new Intent(Intent.ActionMain, null);
                intent.AddCategory(Intent.CategoryLauncher);
                var resolveInfoList = packageManager.QueryIntentActivities(intent, 0);
                foreach (var resolveInfo in resolveInfoList)
                {
                    var deviceAppInfo = new DeviceAppInfo()
                    {
                        AppId = resolveInfo.ActivityInfo.PackageName,
                        AppName = resolveInfo.ActivityInfo.LoadLabel(packageManager),
                        IconPng = EncodeToBase64(resolveInfo.ActivityInfo.LoadIcon(packageManager), 100)
                    };
                    deviceAppInfos.Add(deviceAppInfo);
                }

                return deviceAppInfos.ToArray();
            }
        }

        private static Bitmap DrawableToBitmap(Drawable drawable)
        {
            if (drawable is BitmapDrawable drawable1)
                return drawable1.Bitmap;

            //var bitmap = CreateBitmap(drawable.IntrinsicWidth, drawable.IntrinsicHeight, Config.Argb8888);
            var bitmap = CreateBitmap(32, 32, Config.Argb8888);
            var canvas = new Canvas(bitmap);
            drawable.SetBounds(0, 0, canvas.Width, canvas.Height);
            drawable.Draw(canvas);

            return bitmap;
        }

        private static string EncodeToBase64(Drawable drawable, int quality)
        {
            var bitmap = DrawableToBitmap(drawable);
            var stream = new MemoryStream();
            if (!bitmap.Compress(Bitmap.CompressFormat.Png, quality, stream))
                throw new Exception("Could not compress bitmap to png!");
            return Convert.ToBase64String(stream.ToArray());
        }

        public void VpnPermissionGranted()
        {
            _permissionGranted = true;
            _grantPermisssionWaitHandle.Set();
        }

        public void VpnPermissionRejected()
        {
            _grantPermisssionWaitHandle.Set();
        }

        public static AndroidDevice Current { get; private set; }

        public bool IsExcludeApplicationsSupported => true;

        public bool IsIncludeApplicationsSupported => true;

        public bool IsExcludeNetworksSupported => false;

        public bool IsIncludeNetworksSupported => false;

        public AndroidDevice()
        {
            if (Current != null) throw new InvalidOperationException($"Only one {nameof(AndroidDevice)} can be created!");
            Current = this;
        }

        public Task<IPacketCapture> CreatePacketCapture()
        {
            return Task.Run(() =>
            {
                // Grant for permission if OnRequestVpnPermission is registered otherwise let service throw the error
                if (OnRequestVpnPermission != null)
                {
                    _permissionGranted = false;
                    OnRequestVpnPermission.Invoke(this, EventArgs.Empty);
                    _grantPermisssionWaitHandle.WaitOne(10000);
                    if (!_permissionGranted)
                        throw new Exception("Could not grant VPN permission!");
                }

                StartService();
                _serviceWaitHandle.WaitOne();
                return Task.FromResult(_packetCapture);
            });
        }

        internal void OnServiceStartCommand(IPacketCapture packetCapture, Intent intent)
        {
            _packetCapture = packetCapture;
            _serviceWaitHandle.Set();

            // fire AutoCreate for always on
            var manual = intent?.GetBooleanExtra("manual", false) ?? false;
            if (!manual)
                OnStartAsService?.Invoke(this, EventArgs.Empty);
        }

        private void StartService()
        {
            var intent = new Intent(Application.Context, typeof(AppVpnService));
            intent.PutExtra("manual", true);
            Application.Context.StartService(intent.SetAction("connect"));
        }
    }
}