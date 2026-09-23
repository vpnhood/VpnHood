using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.AppLib.Api.App;

// Whether the app's page and API can be reached from the local network, and how. The web server's
// own answer: the app has no listeners, so it carries nothing about them. The reply of a start,
// and of GET remote-access while the pairing screen is open.
public class RemoteAccessState
{
    // Reachable from the LAN right now: held by a pairing screen, or always-on.
    public required bool IsActive { get; init; }

    // A debug build or the /remote-access command: the app's own listener is on every interface
    // for the whole process, so no screen holds it and closing one changes nothing.
    public required bool IsAlwaysOn { get; init; }

    // Where a phone can dial in, best guess first, each carrying the pairing token the listener
    // asks for on the first hit (none for a developer's always-on). Those of the last start or
    // refresh; empty before a start.
    public required IReadOnlyList<Uri> Urls { get; init; }

    // Devices seen on the LAN within the last few seconds. Presence, not sessions: a paired page
    // polls every second, so a closed tab ages out, and two browsers on one phone count once.
    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public required IPAddress[] ConnectedDevices { get; init; }
}
