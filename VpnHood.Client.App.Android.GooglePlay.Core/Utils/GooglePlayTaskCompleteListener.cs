namespace VpnHood.Client.App.Droid.GooglePlay.Utils;

public class GooglePlayTaskCompleteListener<T> : Java.Lang.Object,
    Xamarin.Google.Android.Play.Core.Tasks.IOnSuccessListener,
    Xamarin.Google.Android.Play.Core.Tasks.IOnFailureListener,
    Xamarin.Google.Android.Play.Core.Tasks.IOnCompleteListener
{
    private readonly TaskCompletionSource<T?> _taskCompletionSource;
    public Task<T?> Task => _taskCompletionSource.Task;
    private GooglePlayTaskCompleteListener(Xamarin.Google.Android.Play.Core.Tasks.Task googlePlayTask)
    {
        _taskCompletionSource = new TaskCompletionSource<T?>();
        googlePlayTask.AddOnSuccessListener(this);
        googlePlayTask.AddOnFailureListener(this);
    }

    public static Task<T?> Create(Xamarin.Google.Android.Play.Core.Tasks.Task googlePlayTask)
    {
        var listener = new GooglePlayTaskCompleteListener<T?>(googlePlayTask);
        return listener.Task;
    }
    public void OnSuccess(Java.Lang.Object? obj)
    {
        switch (obj)
        {
            case null:
                _taskCompletionSource.TrySetResult(default);
                break;

            case T result:
                _taskCompletionSource.TrySetResult(result);
                break;

            default:
                _taskCompletionSource.TrySetException(new Exception(
                    $"Unexpected type in GooglePlayTaskCompleteListener. Expected: {typeof(T)}, Actual: {obj.GetType()}."));
                break;
        }
    }

    public void OnFailure(Java.Lang.Exception ex)
    {
        _taskCompletionSource.TrySetException(ex);
    }

    public void OnComplete(Xamarin.Google.Android.Play.Core.Tasks.Task p0)
    {

    }
}