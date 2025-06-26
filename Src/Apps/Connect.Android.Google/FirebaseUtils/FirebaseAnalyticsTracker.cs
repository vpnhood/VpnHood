using Firebase;
using Firebase.Analytics;
using Firebase.Crashlytics;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.App.Client.Droid.Google.FirebaseUtils;

public class FirebaseAnalyticsTracker : Singleton<FirebaseAnalyticsTracker>, ITracker
{
    private readonly FirebaseAnalytics? _analytics;

    public static void Init()
    {
        _ = new FirebaseAnalyticsTracker();
    }

    public FirebaseAnalyticsTracker()
    {
        try {
            FirebaseApp.InitializeApp(Application.Context);
            _analytics = FirebaseAnalytics.GetInstance(Application.Context);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not initialize Firebase Analytics.");
        }

        try {
            FirebaseCrashlytics.Instance.SetCrashlyticsCollectionEnabled(Java.Lang.Boolean.True);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not enable Firebase Crashlytics collection.");
        }
    }

    public bool IsEnabled { get; set; }

    public Task Track(IEnumerable<TrackEvent> trackEvents, CancellationToken cancellationToken)
    {
        foreach (var trackEvent in trackEvents)
            TrackInternal(trackEvent);

        return Task.CompletedTask;
    }

    public Task Track(TrackEvent trackEvent, CancellationToken cancellationToken)
    {
        TrackInternal(trackEvent);
        return Task.CompletedTask;
    }

    private void TrackInternal(TrackEvent trackEvent)
    {
        if (!IsEnabled || _analytics == null)
            return;

        var bundle = new Bundle();
        foreach (var parameter in trackEvent.Parameters)
            bundle.PutString(parameter.Key, parameter.Value.ToString());

        _analytics.LogEvent(trackEvent.EventName, bundle);
    }
}