using Com.Appsflyer;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.App.Connect.Android.Web.AppsFlyerUtils;

// The process's one AppsFlyer, which reports the install and follows the usage-data switch. It sends
// none of the app's events. AppsFlyer must start from an Activity, so nothing of it runs until the app
// shows one: in the VPN service's process, which has none, it stays idle.
public class AppsFlyerTracker : ITracker
{
    private static readonly Lock InstanceLock = new();
    private static AppsFlyerTracker? _instance;
    private readonly string _devKey;
    private volatile bool _isEnabled;
    private bool _isInit; // the UI thread's alone, as _isStarted
    private bool _isStarted;

    private AppsFlyerTracker(string devKey)
    {
        _devKey = devKey;
        AppUiContext.OnChanged += (_, _) => Apply();
    }

    public static AppsFlyerTracker GetInstance(string devKey)
    {
        lock (InstanceLock)
            return _instance ??= new AppsFlyerTracker(devKey);
    }

    public bool IsEnabled {
        get => _isEnabled;
        set {
            if (_isEnabled == value) return;
            _isEnabled = value;
            Apply();
        }
    }

    public Task Track(IEnumerable<TrackEvent> trackEvents, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Track(TrackEvent trackEvent, CancellationToken cancellationToken) => Task.CompletedTask;

    // Each change runs on the UI thread, one at a time, against the switch as it is by then.
    private void Apply()
    {
        if (AppUiContext.Context is not AndroidUiContext uiContext)
            return; // at the Activity's arrival (OnChanged)

        var activity = uiContext.Activity;
        activity.RunOnUiThread(() => ApplyOnUiThread(activity));
    }

    private void ApplyOnUiThread(Activity activity)
    {
        try {
            if (_isEnabled && !_isStarted)
                Start(activity);
            else if (!_isEnabled && _isStarted)
                Stop(activity);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "AppsFlyer could not follow the usage-data switch.");
        }
    }

    // Init only now, at the first start: AppsFlyer's Init may already fetch its configuration.
    private void Start(Activity activity)
    {
        var appsFlyer = AppsFlyerLib.Instance;
        if (!_isInit) {
            VhLogger.Instance.LogInformation("Starting AppsFlyer.");
            appsFlyer.SetDisableAdvertisingIdentifiers(true);
            appsFlyer.Init(_devKey, null, activity);
            _isInit = true;
        }

        // a stopped AppsFlyer resumes only after its stop is lifted
        if (appsFlyer.IsStopped)
            appsFlyer.Stop(false, activity);

        appsFlyer.Start(activity);
        _isStarted = true;
    }

    private void Stop(Activity activity)
    {
        VhLogger.Instance.LogInformation("Stopping AppsFlyer.");
        AppsFlyerLib.Instance.Stop(true, activity);
        _isStarted = false;
    }
}
