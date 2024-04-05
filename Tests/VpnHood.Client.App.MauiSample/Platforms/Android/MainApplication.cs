using Android.App;
using Android.Runtime;

namespace VpnHood.Client.Samples.MauiAppSpaSample;


[Application]
public class MainApplication(IntPtr handle, JniHandleOwnership ownership)
    : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp()
    {
        return MauiProgram.CreateMauiApp();
    }
}