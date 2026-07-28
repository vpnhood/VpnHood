using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Dtos;

// An option that can push traffic outside the VPN in the CURRENT state — the user's own splits included:
// they are deliberate, but the user still deserves to see every open door in one list. An option that can
// not actually leak right now is not reported: the server's filters count only while unsupported IPs are
// excluded rather than blocked, and only when the server's declaration leaves something out.
// Serialized by name: the UI maps each member to a localized label in the leak dialog.
[JsonConverter(typeof(JsonStringEnumConverter<AppLeakCause>))]
public enum AppLeakCause
{
    // client-side splits, named after the setting that switches each on
    SplitApps,
    SplitCountry,
    SplitIpViaApp,
    SplitIpViaDevice,
    SplitDomain,
    SplitLocalNetwork,

    // the server's own configuration leaves destinations outside the tunnel and SplitUnsupportedIpMode lets
    // them go there; one cause, because which of the server's two declarations is narrow makes no
    // difference to the user, who can only decide the fate of what the server refuses
    ServerSplitTraffic
}
