using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Dtos;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.AppLib;

public class AppState
{
    public required DateTime ConfigTime { get; init; }
    public required AppConnectionState ConnectionState { get; init; }
    public required AppSessionInfo? SessionInfo { get; init; }
    public required AppSessionStatus? SessionStatus { get; init; }
    public required DateTime? ConnectRequestTime { get; init; }
    public required ApiError? LastError { get; init; }
    public required ClientProfileBaseInfo? ClientProfile { get; init; }
    public required bool IsIdle { get; init; }
    public required bool PromptForLog { get; init; }
    public required bool LogExists { get; init; }
    public required bool HasDiagnoseRequested { get; init; }
    public required string? ClientCountryCode { get; init; }
    public required string? ClientCountryName { get; init; }
    public required VersionStatus VersionStatus { get; init; }
    public required PublishInfo? LastPublishInfo { get; init; }
    public required bool CanDisconnect { get; init; }
    public required bool CanConnect { get; init; }
    public required bool CanDiagnose { get; init; }
    public required UiCultureInfo CurrentUiCultureInfo { get; init; }
    public required UiCultureInfo SystemUiCultureInfo { get; init; }
    public required BillingPurchaseState? PurchaseState { get; init; }
    public required SystemBarsInfo SystemBarsInfo { get; init; }
}