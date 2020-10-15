using System;
using Android.App;
using Android.Runtime;
using Android.Telephony;
using Android.Widget;
using VpnHood.Client.App;
using VpnHood.Client.App.UI;

namespace VpnHood.Client.Droid
{
#if DEBUG
    [Application(Debuggable = true, UsesCleartextTraffic = true)]
#else
    [Application(Debuggable = false, UsesCleartextTraffic = true)]
#endif
    class AndroidApp : Application, IAppProvider
    {
        public static AndroidApp Current { get; private set; }
        private VpnHoodApp VpnHoodApp { get; set; }
        private VpnHoodAppUI VpnHoodAppUI { get; set; }
        public MainActivity MainActivity { get; internal set; }
        public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer) 
            : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            base.OnCreate();

            //app init ...
            VpnHoodApp = VpnHoodApp.Init(this, new AppOptions { });
            VpnHoodAppUI = VpnHoodAppUI.Init();
            Current = this;
        }

        public event EventHandler<AppDeviceReadyEventArgs> DeviceReadly;

        public void PrepareDevice()
        {
            MainActivity.StartVpn();
        }

        internal void InvokeDeviceReady(AppVpnService vpnService)
        {
            try
            {
                DeviceReadly?.Invoke(this, new AppDeviceReadyEventArgs(vpnService));
            }
            catch (Exception ex)
            {
                var toast = Toast.MakeText(this, ex.Message, ToastLength.Long);
                toast.Show();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                VpnHoodApp?.Dispose();
                VpnHoodAppUI?.Dispose();
            }

            base.Dispose(disposing);
        }

    }
}