using System;
using Android.App;
using Android.OS;
using Android.Runtime;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Android;

namespace VpnHood.Client.App.Android
{
#if DEBUG
    [Application(Debuggable = true, UsesCleartextTraffic = true, Icon = "@mipmap/ic_launcher")]
#else
    [Application(Debuggable = false, UsesCleartextTraffic = true, Icon = "@mipmap/ic_launcher")]
#endif
    class AndroidApp : Application, IAppProvider
    {
        public static AndroidApp Current { get; private set; }
        private VpnHoodApp VpnHoodApp { get; set; }
        public IDevice Device { get; }

        public string OperatingSystemInfo =>
            $"{Build.Manufacturer}: {Build.Model}, Android: {Build.VERSION.Release}";

        public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
            : base(javaReference, transfer)
        {
            Device = new AndroidDevice();
        }

        public override void OnCreate()
        {
            base.OnCreate();

            //app init
            VpnHoodApp = VpnHoodApp.Init(this, new AppOptions { });
            Current = this;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                VpnHoodApp?.Dispose();
                VpnHoodApp = null;
            }

            base.Dispose(disposing);
        }
    }
}