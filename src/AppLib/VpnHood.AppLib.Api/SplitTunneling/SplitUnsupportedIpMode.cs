using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.SplitTunneling;

// What happens to a destination the server does not route, when no split of the person's own has
// already excluded it. Exclude connects directly and leaks what the server declined; Block drops it.
// Serialized by name: a saved setting as much as a wire value.
[JsonConverter(typeof(JsonStringEnumConverter<SplitUnsupportedIpMode>))]
public enum SplitUnsupportedIpMode
{
    Exclude,
    Block
}
