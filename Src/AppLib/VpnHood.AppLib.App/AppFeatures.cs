using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.AppLib;

public class AppFeatures
{
    public required string AppId { get; init; }
    public required bool IsExcludeAppsSupported { get; init; }
    public required bool IsIncludeAppsSupported { get; init; }
    public required Uri? UpdateInfoUrl { get; init; }
    public required string? UiName { get; init; }
    public required bool IsPremiumFlagSupported { get; init; }
    public required bool IsPremiumFeaturesForced { get; init; }
    public required bool IsAddAccessKeySupported { get; init; }
    public required Guid? BuiltInClientProfileId { get; init; }
    public required bool IsAccountSupported { get; init; }
    public required bool IsBillingSupported { get; init; }
    public required bool IsQuickLaunchSupported { get; init; }
    public required bool IsNotificationSupported { get; init; }
    public required bool IsAlwaysOnSupported { get; init; }
    public required bool IsTv { get; init; }
    public required string? GaMeasurementId { get; init; }
    public required string ClientId { get; init; }
    public required bool IsDebugMode { get; init; }
    public required string[] DebugCommands { get; init; }
    public required bool IsLocalNetworkSupported { get; init; }
    public required bool AdjustForSystemBars { get; init; }
    public required bool AllowEndPointStrategy { get; init; }
    public required object? CustomData { get; init; }

    [JsonConverter(typeof(VersionConverter))]
    public required Version Version { get; init; }

}