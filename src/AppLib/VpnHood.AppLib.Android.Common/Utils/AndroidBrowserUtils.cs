using Android.Content;
using Android.Content.PM;

namespace VpnHood.AppLib.Droid.Common.Utils;

public static class AndroidBrowserUtils
{
    // Android TV ships a placeholder in place of a browser. It ANSWERS an https view — so resolving
    // one finds a handler and nothing opens — which is why the stub's package is excluded by name
    // rather than trusted like any other result. It is also why this is asked of the system at all
    // instead of inferred from "is this a TV": a television with a browser installed can open a
    // page, and must not be told it cannot.
    private const string TvStubsPackage = "com.android.tv.frameworkpackagestubs";

    /// <summary>Whether an ordinary web page can be handed to an external browser on this device.</summary>
    public static bool IsExternalBrowserAvailable()
    {
        var packageManager = Application.Context.PackageManager;
        if (packageManager == null)
            return false;

        using var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse("https://vpnhood.com"));
        var componentName = intent.ResolveActivity(packageManager);
        return componentName != null && componentName.PackageName != TvStubsPackage;
    }
}
