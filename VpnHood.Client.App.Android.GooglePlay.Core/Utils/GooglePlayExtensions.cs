namespace VpnHood.Client.App.Droid.GooglePlay.Utils;

public static class GooglePlayExtensions
{
    public static Task<T?> AsTask<T>(this Xamarin.Google.Android.Play.Core.Tasks.Task googlePlayTask) where T : class
    {
        return GooglePlayTaskCompleteListener<T>.Create(googlePlayTask);
    }

    public static Task AsTask(this Xamarin.Google.Android.Play.Core.Tasks.Task googlePlayTask)
    {
        return GooglePlayTaskCompleteListener<object>.Create(googlePlayTask);
    }

}