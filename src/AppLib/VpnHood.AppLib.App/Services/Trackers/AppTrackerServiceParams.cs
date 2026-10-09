using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

namespace VpnHood.AppLib.App.Services.Trackers;

// What AppTrackerService is made with: the head's factories and what the app knows of itself.
public class AppTrackerServiceParams
{
    public required IReadOnlyList<ITrackerFactory> TrackerFactories { get; init; }
    public required bool IsDebugMode { get; init; }
    public required bool IsLicenseAgreementRequired { get; init; }
    public required string ClientId { get; init; }
    public required Version AppVersion { get; init; }
}
