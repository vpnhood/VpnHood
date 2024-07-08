namespace VpnHood.Client.App;

public class AppFeatures
{
    public required Version Version { get; init; }
    public required bool IsExcludeAppsSupported { get; init; }
    public required  bool IsIncludeAppsSupported { get; init; }
    public required Uri? UpdateInfoUrl { get; init; }
    public required string? UiName { get; init; }
    public required bool IsAddAccessKeySupported { get; init; }
    public required Guid? BuiltInClientProfileId { get; init; }
    public required bool IsAccountSupported { get; init; }
    public required bool IsBillingSupported { get; init; }
    public required bool IsQuickLaunchSupported { get; init; }
    public required bool IsNotificationSupported { get; init; }
    public required bool IsAlwaysOnSupported { get; init; }
    public required string? GaMeasurementId { get; init; }
    public required string ClientId { get; init; }
}