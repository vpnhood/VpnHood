using System.Text.Json.Serialization;

namespace VpnHood.Common.Messaging
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SessionSuppressType
    {
        None,
        YourSelf,
        Other
    }
}