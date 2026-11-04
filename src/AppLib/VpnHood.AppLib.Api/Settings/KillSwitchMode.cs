using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Settings;

// The kill switch's level, where the device has one (IDevice.KillSwitch). One setting, not a switch with an option
// under it: a lockdown with the switch off would be a fourth state that does nothing.
[JsonConverter(typeof(JsonStringEnumConverter<KillSwitchMode>))]
public enum KillSwitchMode
{
    Off,

    // Nothing leaves outside the tunnel from a connect until a person disconnects, through an error stop, a crash
    // and a reboot.
    UntilDisconnect,

    // As UntilDisconnect, and the block stays after a person's disconnect too, by the settings' split; chosen while
    // disconnected, it goes up at once, and only another level lifts it. A hotel's Wi-Fi sign-in page needs another
    // level first.
    Lockdown
}
