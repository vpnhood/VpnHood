using Android.Content;
using Firebase;
using Firebase.Crashlytics;
using Boolean = Java.Lang.Boolean;

namespace VpnHood.Client.App.Droid.GooglePlay;

public class GooglePlayFirebaseCrashlytics
{
    public GooglePlayFirebaseCrashlytics(Context context)
    {
        var firebaseApp = FirebaseApp.InitializeApp(context);
        firebaseApp.SetDataCollectionDefaultEnabled(Boolean.True);
        FirebaseCrashlytics.Instance.SetCrashlyticsCollectionEnabled(true);
    }

    public static GooglePlayFirebaseCrashlytics Create(Context context) 
    {
        return new GooglePlayFirebaseCrashlytics(context);
    }
}