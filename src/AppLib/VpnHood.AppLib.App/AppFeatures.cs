using System.Text.Json.Serialization;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.AppLib;

public class AppFeatures
{
    public required string AppId { get; init; }
    public required string AppName { get; init; }
    public required bool IsExcludeAppsSupported { get; init; }
    public required bool IsIncludeAppsSupported { get; init; }
    public required string? UiName { get; init; }
    public required bool IsAddAccessKeySupported { get; init; }

    // This build's premium tier, or null when the product has none. Null is not "premium locked" —
    // it is the FULL app: every premium-listed feature allowed, nothing sold, no crown, no page that
    // promotes or bills, whatever routes the server's client policy offers. One nullable block
    // rather than loose flags so a head cannot declare "no premium, but codes are fine": the code
    // and purchase-url capabilities only exist inside a tier that gives them meaning.
    public required AppPremiumOptions? Premium { get; init; }
    public required bool IsAccountSupported { get; init; }
    public required bool IsBillingSupported { get; init; }
    // Identity provider ids (see AuthProviders), self-declared by the app's auth provider — named
    // for what they identify, because this class also stands in front of the ad, updater, culture
    // and device-UI providers. Strings, not an enum: third-party providers declare their own ids
    // and the UI derives labels by convention (SIGN_IN_WITH_<UPPERCASE-ID>).
    public required IReadOnlyList<string> AuthProviderIds { get; init; }

    // The human account website behind the auth provider — the password form's escape hatch
    // ("forgot password?" opens it in the device browser). Null when no provider declares one.
    public required Uri? AccountWebsiteUrl { get; init; }
    public required bool IsTcpProxySupported { get; init; }
    public required bool IsQuicSupported { get; init; }
    public required bool IsSplitDomainSupported { get; init; }
    public required bool IsUserReviewSupported { get; init; }

    // Whether this build collects anonymous data at all: analytics events, and the crash reports that ride
    // with them where the tracker is a crash-reporting one (Connect's Firebase tracker enables Crashlytics).
    // Derived from the tracker that could actually be created — no measurement id, or a debug build, leaves
    // a NullTracker — so the UI drops the privacy consent in a build that collects nothing instead of
    // asking which product is running. The user's choice is UserSettings.AllowAnonymousTracker.
    public required bool IsAnonymousTrackerSupported { get; init; }
    public required bool IsTv { get; init; }
    public required AppOsType OsType { get; init; }
    public required string? GaMeasurementId { get; init; }
    public required string ClientId { get; init; }
    public required bool IsDebugMode { get; init; }
    public required string[] DebugCommands { get; init; }
    public required bool IsProxySupported { get; init; }
    public required bool AdjustForSystemBars { get; init; }
    public required bool AllowEndPointStrategy { get; init; }
    public required bool IsAdSupported { get; set; }
    public required bool IsRewardedAdSupported { get; init; }
    public required int? WebUiPort { get; set; }
    public required IReadOnlyList<ChannelProtocol> ChannelProtocols { get; init; }
    public required object? CustomData { get; init; }

    [JsonConverter(typeof(VersionConverter))]
    public required Version Version { get; init; }
}