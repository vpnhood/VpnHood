using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.App;

/// <summary>Specifies where always-on stands, where the app keeps it itself.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AlwaysOnStatus>))]
public enum AlwaysOnStatus
{
    /// <summary>Always-on is off.</summary>
    Off,

    /// <summary>Always-on is on, with nothing to wait for: a session is up or on its way, or none has failed since.</summary>
    On,

    /// <summary>A connect failed to reach a working server, and is tried again at <see cref="AlwaysOnState.RetryTime"/>.</summary>
    Waiting,

    /// <summary>
    /// Something else stopped it, until a person connects; a stop by an error also ends when the plan changes or the
    /// service restarts, but one by another VPN app taking the device's VPN lasts through a restart.
    /// </summary>
    Stopped
}
