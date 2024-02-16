using Xamarin.Google.Android.Play.Core.Tasks;
using Exception = Java.Lang.Exception;
using Object = Java.Lang.Object;
using Task = Xamarin.Google.Android.Play.Core.Tasks.Task;

namespace VpnHood.Client.App.Droid.GooglePlay;

public class GooglePlayTaskCompleteListener<T> : Object,
    IOnSuccessListener,
    IOnFailureListener
{
    private readonly TaskCompletionSource<T> _taskCompletionSource;
    public Task<T> Task => _taskCompletionSource.Task;

    public GooglePlayTaskCompleteListener(Task appUpdateInfo)
    {
        _taskCompletionSource = new TaskCompletionSource<T>();
        appUpdateInfo.AddOnSuccessListener(this);
        appUpdateInfo.AddOnFailureListener(this);
    }

    public void OnSuccess(Object? obj)
    {
        if (obj is T result)
            _taskCompletionSource.TrySetResult(result);
        else
            _taskCompletionSource.TrySetException(new System.Exception($"Unexpected type: {obj?.GetType()}."));
    }

    public void OnFailure(Exception ex)
    {
        _taskCompletionSource.TrySetException(ex);
    }
}