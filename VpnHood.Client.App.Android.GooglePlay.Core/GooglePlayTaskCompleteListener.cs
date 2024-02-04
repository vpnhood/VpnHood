namespace VpnHood.Client.App.Droid.GooglePlay;

public class GooglePlayTaskCompleteListener<T> : Java.Lang.Object,
    Xamarin.Google.Android.Play.Core.Tasks.IOnSuccessListener,
    Xamarin.Google.Android.Play.Core.Tasks.IOnFailureListener
{
    private readonly TaskCompletionSource<T> _taskCompletionSource;
    public Task<T> Task => _taskCompletionSource.Task;

    public GooglePlayTaskCompleteListener(Xamarin.Google.Android.Play.Core.Tasks.Task appUpdateInfo)
    {
        _taskCompletionSource = new TaskCompletionSource<T>();
        appUpdateInfo.AddOnSuccessListener(this);
        appUpdateInfo.AddOnFailureListener(this);
    }

    public void OnSuccess(Java.Lang.Object? obj)
    {
        if (obj is T result)
            _taskCompletionSource.TrySetResult(result);
        else
            _taskCompletionSource.TrySetException(new Exception($"Unexpected type: {obj?.GetType()}."));
    }

    public void OnFailure(Java.Lang.Exception ex)
    {
        _taskCompletionSource.TrySetException(ex);
    }
}