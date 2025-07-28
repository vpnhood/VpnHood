using VpnHood.Core.Common.Tokens;

namespace VpnHood.Core.Client;

public class VpnHoodClientSettings
{
    public int MaxProtocolVersion => 9;
    public int MinProtocolVersion => 4;
    public required Version Version { get; init; }
    public required bool AutoDisposeVpnAdapter { get; init; }
    public required int MaxPacketChannelCount { get; init; }
    public required TimeSpan MinPacketChannelLifespan { get; init; }
    public required TimeSpan MaxPacketChannelLifespan { get; init; }
    public required bool AllowAnonymousTracker { get; init; }
    public required TimeSpan TcpConnectTimeout { get; init; }
    public required ConnectPlanId PlanId { get; init; }
    public required string? AccessCode { get; init; }
    public required bool AllowRewardedAd { get; init; }
    public required TimeSpan CanExtendByRewardedAdThreshold { get; init; }
    public required string[]? IncludeApps { get; init; }
    public required string[]? ExcludeApps { get; init; }
    public required TimeSpan SessionTimeout { get; init; }
    public required TimeSpan AutoWaitTimeout { get; init; }
    public required TimeSpan ReconnectTimeout { get; init; }
    public required string ClientId { get; init; }
    public required bool IncludeLocalNetwork { get; init; }
    public required string? SessionName { get; init; }
    public required string UserAgent { get; init; }
    public required bool AllowTcpReuse { get; init; }
    public required bool DropUdp { get; set; }
    public required bool DropQuic { get; set; }
    public required bool UseTcpOverTun { get; set; }
}