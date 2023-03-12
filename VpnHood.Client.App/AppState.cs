using System;
using VpnHood.Common.Messaging;

namespace VpnHood.Client.App;

public class AppState
{
    public AppConnectionState ConnectionState { get; internal set; }
    public string? LastError { get; internal set; }
    public Guid? ActiveClientProfileId { get; internal set; }
    public Guid? DefaultClientProfileId { get; internal set; }
    public bool IsIdle { get; internal set; }
    public bool LogExists { get; internal set; }
    public Guid? LastActiveClientProfileId { get; internal set; }
    public bool HasDiagnoseStarted { get; internal set; }
    public bool HasDisconnectedByUser { get; internal set; }
    public bool HasProblemDetected { get; internal set; }
    public SessionStatus? SessionStatus { get; internal set; }
    public Traffic Speed { get; internal set; } = new ();
    public Traffic SessionTraffic { get; internal set; } = new ();
    public Traffic AccountTraffic { get; internal set; } = new ();
    public IpGroup? ClientIpGroup { get; internal set; }
    public bool IsWaitingForAd { get; internal set; }
    public VersionStatus VersionStatus { get; internal set; }
    public PublishInfo? LastPublishInfo { get; internal set; }
}