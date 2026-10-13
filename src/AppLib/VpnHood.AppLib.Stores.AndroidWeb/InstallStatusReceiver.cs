using Android.Content;

namespace VpnHood.AppLib.Stores.AndroidWeb;

// The installer's answer to a committed session, registered by the provider while the app runs: the
// person presses Install with the app in front, so a receiver in the manifest would add nothing.
internal class InstallStatusReceiver(AndroidWebAppUpdaterProvider provider) : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context != null && intent != null)
            provider.OnInstallStatus(context, intent);
    }
}
