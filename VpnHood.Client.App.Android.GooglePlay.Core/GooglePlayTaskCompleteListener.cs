
namespace VpnHood.Client.App.Droid.GooglePlay;

public class GooglePlayTaskCompleteListener<T> : Java.Lang.Object,
    Xamarin.Google.Android.Play.Core.Tasks.IOnSuccessListener,
    Xamarin.Google.Android.Play.Core.Tasks.IOnFailureListener,
    Xamarin.Google.Android.Play.Core.Tasks.IOnCompleteListener where T : class
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
        var listener = new GooglePlayTaskCompleteListener<T>(googlePlayTask);
        return listener.Task;
    }
    public void OnSuccess(Java.Lang.Object? obj)
    {
        if (obj is T result)
            _taskCompletionSource.TrySetResult(result);
        else
        {
            Console.WriteLine("ZZZ 5");
            _taskCompletionSource.SetException(new System.Exception("test"));
            //_taskCompletionSource.TrySetException(new System.Exception($"Unexpected type: {obj?.GetType()}."));
            Console.WriteLine($"ZZZ {_taskCompletionSource.Task.Exception}");
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