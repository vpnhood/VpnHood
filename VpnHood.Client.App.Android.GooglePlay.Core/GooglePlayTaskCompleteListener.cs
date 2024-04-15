using Xamarin.Google.Android.Play.Core.Install;
using Xamarin.Google.Android.Play.Core.Tasks;
using Exception = Java.Lang.Exception;
using Object = Java.Lang.Object;
using Task = Xamarin.Google.Android.Play.Core.Tasks.Task;

namespace VpnHood.Client.App.Droid.GooglePlay;

public class GooglePlayTaskCompleteListener<T> : Object,
    IOnSuccessListener,
    IOnFailureListener,
    IOnCompleteListener
{
    private readonly TaskCompletionSource<T> _taskCompletionSource;
    public Task<T> Task => _taskCompletionSource.Task;

    public GooglePlayTaskCompleteListener(Task googlePlayTask)
    {
        _taskCompletionSource = new TaskCompletionSource<T>();
        googlePlayTask.AddOnSuccessListener(this);
        googlePlayTask.AddOnFailureListener(this);
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

    public void OnComplete(Task p0)
    {
        
    }
}