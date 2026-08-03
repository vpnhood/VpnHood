using Firebase;
using Firebase.Analytics;
using Firebase.Crashlytics;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.App.Connect.Droid.Google.FirebaseUtils;

// This one instance owns everything this build sends to Firebase: analytics events and crash reports.
// Both follow the user's consent through IsEnabled — the app has a single "Share anonymous usage data"
// switch and nothing here may outlive it.
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

        // Nothing is forced on here. Firebase auto-inits from a ContentProvider before Application.OnCreate
        // and starts from its own persisted state, which is what IsEnabled last wrote: a user who opted out
        // stays opted out from the very first instruction of the next launch, and a fresh install collects
        // until consent is applied — deliberately, so a crash during that first startup is still reported.
    }

    // Both SDKs follow the user's consent (VpnHoodApp applies UserSettings.AllowAnonymousTracker here), and
    // it is applied to the SDKs themselves rather than only to our Track calls below: Firebase collects
    // first_open/session_start/screen_view and crash reports on its own, which no amount of
    // not-calling-LogEvent would suppress. Firebase persists both values for subsequent launches.
    // The only window neither switch can cover is the first startup of a fresh install, before the setting
    // has been read — the privacy policy says so.
    public bool IsEnabled {
        get;
        set {
            field = value;
            try {
                _analytics?.SetAnalyticsCollectionEnabled(value);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not change the Firebase Analytics collection state.");
            }

            try {
                FirebaseCrashlytics.Instance.SetCrashlyticsCollectionEnabled(
                    value ? Java.Lang.Boolean.True : Java.Lang.Boolean.False);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not change the Firebase Crashlytics collection state.");
            }
        }
    }

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
            bundle.PutString(parameter.Key, parameter.Value?.ToString());

        _analytics.LogEvent(trackEvent.EventName, bundle);
    }
}