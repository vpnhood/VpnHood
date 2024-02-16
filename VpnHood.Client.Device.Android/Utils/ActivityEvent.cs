using Android.Content;
using Android.Runtime;

namespace VpnHood.Client.Device.Droid.Utils;

public class ActivityEvent : Activity, IActivityEvent
{
    public event EventHandler<ActivityResultEventArgs>? OnActivityResultEvent;
    public event EventHandler? OnDestroyEvent;

    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        OnActivityResultEvent?.Invoke(this, new ActivityResultEventArgs
        {
            RequestCode = requestCode,
            ResultCode = resultCode,
            Data = data
        });

        base.OnActivityResult(requestCode, resultCode, data);
    }
    protected override void OnDestroy()
    {
        OnDestroyEvent?.Invoke(this, EventArgs.Empty);
        base.OnDestroy();
    }

}