using Firebase;
using Firebase.Analytics;
using Firebase.Crashlytics;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.App.Connect.Droid.Google.FirebaseUtils;

// This one instance serves both purposes: it logs analytics events, which honour the user's consent through
// IsEnabled, and it enables Crashlytics, which does not — Firebase installs the crash handler from a
// ContentProvider before Application.OnCreate, so no setting can be read in time. Crash reporting is
// therefore unconditional here, and the privacy policy says so rather than the UI offering a dead switch.
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

    // Follows the user's consent (VpnHoodApp applies UserSettings.AllowAnonymousTracker here), and applies
    // it to the SDK itself rather than only to our Track calls below: Firebase collects first_open,
    // session_start and screen_view on its own, which no amount of not-calling-LogEvent would suppress.
    // Collection is deliberately left ON at startup (no firebase_analytics_collection_enabled meta-data):
    // Firebase auto-inits before any setting can be read, so the alternative was starting dark on first
    // launch. The cost is that a user who then opts out has already sent that launch's automatic events —
    // the privacy policy has to say so. Firebase persists whatever is set here for later launches.
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