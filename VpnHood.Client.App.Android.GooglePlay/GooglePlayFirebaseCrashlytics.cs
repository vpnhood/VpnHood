using Android.Content;
using Firebase;


namespace VpnHood.Client.App.Droid.GooglePlay;

public class GooglePlayFirebaseCrashlytics
{
    public GooglePlayFirebaseCrashlytics(Context context)
    {
        var firebaseApp = FirebaseApp.InitializeApp(context);
        firebaseApp.SetDataCollectionDefaultEnabled(Java.Lang.Boolean.True);
        Firebase.Crashlytics.FirebaseCrashlytics.Instance.SetCrashlyticsCollectionEnabled(true);
    }

    public static GooglePlayFirebaseCrashlytics Create(Context context) 
    {
        return new GooglePlayFirebaseCrashlytics(context);
    }
}