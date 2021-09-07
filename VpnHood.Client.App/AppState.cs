using System;

namespace VpnHood.Client.App
{
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
        public long SendSpeed { get; internal set; }
        public long SentTraffic { get; internal set; }
        public long ReceiveSpeed { get; internal set; }
        public long ReceivedTraffic { get; internal set; }
    }
}