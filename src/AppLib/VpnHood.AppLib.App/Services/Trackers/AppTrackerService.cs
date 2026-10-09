using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.Settings;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Trackers;

namespace VpnHood.AppLib.App.Services.Trackers;

// The app's trackers and when they report: one from each of the head's factories, none in a Debug
// build, switched by the first-run terms where the head asks for them and by the usage-data switch.
// The VPN service makes its own from the same factories, with what this says it may.
public class AppTrackerService
{
    private readonly AppSettingsService _settingsService;
    private readonly bool _isLicenseAgreementRequired;
    private readonly string _clientId;
    private readonly Lock _firstLaunchLock = new();

    public ITracker Tracker { get; }

    // whether this build collects anything: a tracker that is not a null one
    public bool IsSupported { get; }

    // the factories as the VPN service makes them again, in its own process
    public IReadOnlyList<TrackerFactoryInfo> TrackerFactoryInfos { get; }

    public AppTrackerService(AppSettingsService settingsService, AppTrackerServiceParams serviceParams)
    {
        _settingsService = settingsService;
        _isLicenseAgreementRequired = serviceParams.IsLicenseAgreementRequired;
        _clientId = serviceParams.ClientId;

        // a Debug build reports nothing, whatever the head lists
        IReadOnlyList<ITrackerFactory> factories = serviceParams.IsDebugMode ? [] : serviceParams.TrackerFactories;
        TrackerFactoryInfos = factories.Select(TrackerFactorySerializer.Serialize).ToArray();

        var createParams = new TrackerCreateParams {
            ClientId = serviceParams.ClientId,
            ClientVersion = serviceParams.AppVersion,
            UserAgent = null, // the UI gives it at the connect (SetUserAgent)
            IsEnabled = IsAllowed
        };
        Tracker = factories.TryCreateTracker(createParams) ?? NullTrackerFactory.CreateNullTracker(createParams);
        IsSupported = Tracker is not NullTracker;
    }

    // What the VPN service is told: the same, and off where this build has no tracker (iOS, a Debug
    // build), which keeps the access manager's own hit off there too.
    public bool IsVpnServiceTrackerAllowed => IsSupported && IsAllowed;

    // the first-run terms where the head asks for them, then the usage-data switch
    private bool IsAllowed {
        get {
            var userSettings = _settingsService.UserSettings;
            return userSettings.AllowAnonymousTracker && (!_isLicenseAgreementRequired || userSettings.IsLicenseAccepted);
        }
    }

    // At each settings save; a tracker turning on sends the first launch, if it is still due.
    public void ApplySettings()
    {
        var wasEnabled = Tracker.IsEnabled;
        Tracker.IsEnabled = IsAllowed;
        if (Tracker.IsEnabled && !wasEnabled)
            _ = Task.Run(TrackFirstLaunch);
    }

    // The first launch, with the locale-based country, once and only while the tracker is on. The flag is
    // taken before the send, so the start and a switch turning on cannot both send it; a send that fails
    // is not tried again.
    public async Task TrackFirstLaunch()
    {
        lock (_firstLaunchLock) {
            if (_settingsService.Settings.IsStartupTrackerSent || !Tracker.IsEnabled)
                return;

            _settingsService.Settings.IsStartupTrackerSent = true;
        }

        try {
            _settingsService.Save();
            var countryCode = AppRegionInfo.CurrentRegion.Name;
            await Tracker.Track(AppTrackerBuilder.BuildFirstLaunch(_clientId, countryCode), CancellationToken.None).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not send the first launch.");
        }
    }

    // The built-in trackers send the UI's user agent, once the UI has given it, while the switch is on.
    public void SetUserAgent(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent) || !_settingsService.UserSettings.AllowAnonymousTracker)
            return;

        IReadOnlyList<ITracker> trackers = Tracker is CompositeTracker compositeTracker
            ? compositeTracker.Trackers
            : [Tracker];

        foreach (var trackerBase in trackers.OfType<TrackerBase>())
            trackerBase.UserAgent = userAgent;
    }
}
