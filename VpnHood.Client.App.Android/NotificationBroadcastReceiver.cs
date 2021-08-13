#nullable enable
using Android.Content;

namespace VpnHood.Client.App.Android
{
    [BroadcastReceiver(Enabled = true, Exported = false)]
    public class NotificationBroadcastReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            switch (intent?.Action)
            {
                case "disconnect":
                    VpnHoodApp.Current?.Disconnect(true);
                    return;
            }
        }
    }
}