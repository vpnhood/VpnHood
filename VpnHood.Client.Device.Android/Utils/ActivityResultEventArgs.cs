using Android.Content;

namespace VpnHood.Client.Device.Droid.Utils;

public class ActivityResultEventArgs : EventArgs
{
    public int RequestCode { get; init; }

    public Result ResultCode { get; init; }

    public Intent? Data { get; init; }
}