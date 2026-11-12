using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Settings;

/// <summary>Specifies the kill switch's level, where the app keeps a kill switch of its own.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<KillSwitchMode>))]
public enum KillSwitchMode
{
    /// <summary>No kill switch.</summary>
    Off,

    /// <summary>
    /// From a connect until a person disconnects, through an error stop, a crash and a reboot, nothing goes outside the
    /// tunnel but what the split tunneling sends there: the VPN service's own traffic for the splits by country, IP range
    /// and domain while it is connected, the ranges the device adapter leaves out, and the local network where it is split.
    /// </summary>
    UntilDisconnect,

    /// <summary>
    /// As <see cref="UntilDisconnect"/>, and the block stays after a person's disconnect too, by the settings' split; chosen
    /// while disconnected, it goes up at once, and only another level lifts it. A hotel's Wi-Fi sign-in page needs another
    /// level first.
    /// </summary>
    Lockdown
}
