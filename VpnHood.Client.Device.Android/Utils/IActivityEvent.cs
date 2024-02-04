namespace VpnHood.Client.Device.Droid.Utils;

public interface IActivityEvent
{
    event EventHandler<ActivityResultEventArgs> OnActivityResultEvent;
    event EventHandler OnDestroyEvent;
}