﻿using System;
using Android.App;
using Android.Runtime;
using Android.Widget;
using VpnHood.Client.App.UI;
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