using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.App;

// On: switched on, with nothing to wait for - a session is up or on its way, or none has failed since.
// Waiting: a failure to reach a working server, tried again at AlwaysOnState.RetryTime.
// Stopped: by anything else, until a person connects, the plan changes or the service restarts.
[JsonConverter(typeof(JsonStringEnumConverter<AlwaysOnStatus>))]
public enum AlwaysOnStatus
{
    Off,
    On,
    Waiting,
    Stopped
}
