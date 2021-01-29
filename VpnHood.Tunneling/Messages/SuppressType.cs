using System.Text.Json.Serialization;

namespace VpnHood.Tunneling.Messages
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SuppressType
    {
        None,
        YourSelf,
        Other,
    }
}
