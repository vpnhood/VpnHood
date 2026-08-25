using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib;

// What this build decided at construction and never changes. Unlike AppFeatures this is not a DTO
// for the UI — it is internal behavior, never serialized. ConnectTimeout is the whole-connect
// deadline (tripled when diagnosing); the per-TCP connect timeout is Transport.TcpConnectTimeout.
public class VpnHoodAppConfig
{
    public required bool DisconnectOnDispose { get; init; }
    public required bool AutoDiagnose { get; init; }
    public required bool AllowEndPointTracker { get; init; }
    public required bool AllowRecommendUserReviewByServer { get; init; }
    public required TimeSpan ConnectTimeout { get; init; }
    public required LogServiceOptions LogServiceOptions { get; init; }
    public required string? TrackerFactoryAssemblyQualifiedName { get; init; }
    public required AppTransportConfig Transport { get; init; }
}
