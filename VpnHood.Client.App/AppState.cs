using System;
using System.Text.Json.Serialization;
using VpnHood.Client.Diagnosing;

namespace VpnHood.Client.App
{
    public class AppState
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ClientState ClientState { get; internal set; }
        public string LastError { get; internal set; }
        public Guid? ActiveClientProfileId { get; internal set; }
        public bool IsIdle { get; internal set; }
        public bool LogExists { get; internal set; }
        public Guid? LastActiveClientProfileId { get; internal set; }
        public bool HasDiagnoseStarted { get; internal set; }
        public bool IsDiagnosing { get; internal set; }
        public bool HasDisconnectedByUser { get; internal set; }
        public bool HasProblemDetected { get; internal set; }
    }
}
