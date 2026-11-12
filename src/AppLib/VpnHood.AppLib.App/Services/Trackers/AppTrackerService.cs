using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.Settings;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Trackers;

namespace VpnHood.AppLib.App.Services.Trackers;

/// <summary>
/// Provides the app's trackers and decides when they report: never in a Debug build, and only as the first-run terms
/// and the usage-data switch allow; in the European Economic Area, the UK and Switzerland, and everywhere on a build
/// that asks for an opt-in, only once the person said yes.
/// </summary>
public class AppTrackerService
{
    // Where the law wants a yes before usage data - the EEA, the UK and Switzerland - as the app's region
    // tells (AppRegionInfo: the country a server last reported, else the device's): the first-run page
    // asks there, with the box unticked, on every build that reports; everywhere on a build whose channel
    // wants an opt-in (AppOptions.IsAnonymousTrackerOptInRequired).
    private static readonly string[] PromptCountryCodes = [
        "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE", "IT", "LV",
        "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE", // the EU
        "IS", "LI", "NO", "GB", "CH" // the rest of the EEA, the UK and Switzerland
    ];

    private readonly AppSettingsService _settingsService;
    // what the build asks for, not the value of the property that reads each, so no backing field of it
    // ReSharper disable ReplaceWithFieldKeyword
    private readonly bool _isLicenseAgreementRequired;
    private readonly bool _isOptInRequired;
    // ReSharper restore ReplaceWithFieldKeyword
    private readonly string _clientId;
    private readonly Lock _firstLaunchLock = new();

    internal ITracker Tracker { get; }

    // whether this build collects anything: a tracker that is not a null one
    internal bool IsSupported { get; }

    // the factories as the VPN service makes them again, in its own process, and the user agent they send
    internal IReadOnlyList<TrackerFactoryInfo> TrackerFactoryInfos { get; }
    internal string UserAgent { get; }

    internal AppTrackerService(AppSettingsService settingsService, AppTrackerServiceParams serviceParams)
    {
        _settingsService = settingsService;
        _isLicenseAgreementRequired = serviceParams.IsLicenseAgreementRequired;
        _isOptInRequired = serviceParams.IsAnonymousTrackerOptInRequired;
        _clientId = serviceParams.ClientId;

        // a Debug build reports nothing, whatever the head lists
        IReadOnlyList<ITrackerFactory> factories = serviceParams.IsDebugMode ? [] : serviceParams.TrackerFactories;
        TrackerFactoryInfos = factories.Select(TrackerFactorySerializer.Serialize).ToArray();
        UserAgent = serviceParams.UserAgent;

        var createParams = new TrackerCreateParams {
            ClientId = serviceParams.ClientId,
            ClientVersion = serviceParams.AppVersion,
            UserAgent = UserAgent,
            IsEnabled = IsAllowed
        };
        Tracker = factories.TryCreateTracker(createParams) ?? NullTrackerFactory.CreateNullTracker(createParams);
        IsSupported = Tracker is not NullTracker;
    }

    // What the VPN service is told: the same, and off where this build has no tracker (iOS, a Debug
    // build), which keeps the access manager's own hit off there too.
    internal bool IsVpnServiceTrackerAllowed => IsSupported && IsAllowed;

    // the first-run page must ask before this build reports anything
    internal bool IsPromptRequired => IsSupported && IsPromptPending;

    private bool IsPromptPending =>
        !_settingsService.UserSettings.IsAnonymousTrackerPrompted &&
        (_isOptInRequired ||
         PromptCountryCodes.Contains(AppRegionInfo.CurrentRegion.TwoLetterISORegionName, StringComparer.OrdinalIgnoreCase));

    // the first-run terms where the head asks for them, the answer where the law wants one, then the
    // usage-data switch
    private bool IsAllowed {
        get {
            var userSettings = _settingsService.UserSettings;
            return userSettings.AllowAnonymousTracker &&
                   (!_isLicenseAgreementRequired || userSettings.IsLicenseAccepted) &&
                   !IsPromptPending;
        }
    }

    // At each settings save; a tracker turning on sends the first launch, if it is still due.
    internal void ApplySettings()
    {
        var wasEnabled = Tracker.IsEnabled;
        Tracker.IsEnabled = IsAllowed;
        if (Tracker.IsEnabled && !wasEnabled)
            _ = Task.Run(() => TrackFirstLaunch(CancellationToken.None));
    }

    // The first launch, with the locale-based country, once and only while the tracker is on. The flag is
    // taken before the send, so the start and a switch turning on cannot both send it; a send that fails
    // is not tried again.
    internal async Task TrackFirstLaunch(CancellationToken cancellationToken)
    {
        lock (_firstLaunchLock) {
            if (_settingsService.Settings.IsStartupTrackerSent || !Tracker.IsEnabled)
                return;

            _settingsService.Settings.IsStartupTrackerSent = true;
        }

        try {
            _settingsService.Save();
            var countryCode = AppRegionInfo.CurrentRegion.Name;
            await Tracker.Track(AppTrackerBuilder.BuildFirstLaunch(_clientId, countryCode), cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not send the first launch.");
        }
    }
}
