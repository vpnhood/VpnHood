using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.AppLib.Api.App;

/// <summary>
/// Represents whether the app's page and API can be reached from the local network, and how, as the remote access's
/// start and the pairing screen's poll answer it.
/// </summary>
public class RemoteAccessState
{
    /// <summary>Gets a value indicating whether the local network can reach the app now: a pairing screen holds it, or it is always on.</summary>
    public required bool IsActive { get; init; }

    /// <summary>
    /// Gets a value indicating whether the listener is on for the whole run, as in a debug build or with the
    /// <c>/remote-access</c> command, so no screen holds it and closing one changes nothing.
    /// </summary>
    public required bool IsAlwaysOn { get; init; }

    /// <summary>
    /// Gets where a phone can reach the app, best first, each with the pairing token the listener asks for at the first
    /// request (none where it is always on); those of the last start or refresh, none before a start.
    /// </summary>
    public required IReadOnlyList<Uri> Urls { get; init; }

    /// <summary>
    /// Gets the devices seen on the local network within the last few seconds: a paired page polls every second, so a
    /// closed tab ages out, and two browsers on one phone count once.
    /// </summary>
    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public required IPAddress[] ConnectedDevices { get; init; }
}
