﻿using Android.Content;

namespace VpnHood.Client.App.Droid.Common;

[BroadcastReceiver(Enabled = true, Exported = false)]
public class NotificationBroadcastReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        switch (intent?.Action) {
            case "disconnect":
                if (VpnHoodApp.IsInit)
                    _ = VpnHoodApp.Instance.Disconnect(true);

                var notificationManager = (NotificationManager?)context?.GetSystemService(Context.NotificationService);
                notificationManager?.CancelAll();
                return;
        }
    }
}