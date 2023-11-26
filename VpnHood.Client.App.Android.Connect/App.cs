using System;
using Android.App;
using Android.Runtime;
using VpnHood.Client.App.Droid.Common;

namespace VpnHood.Client.App.Droid.Connect;

public class App : VpnHoodAndroidApp
{
    public App(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}