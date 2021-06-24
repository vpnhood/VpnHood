using System.Text.Json.Serialization;

namespace VpnHood.Tunneling.Messages
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ResponseCode
    {
        Ok,
        GeneralError,
        InvalidSessionId,
        SessionClosed,
        SessionSuppressedBy,
        AccessExpired,
        AccessTrafficOverflow,
        UnsupportedClient,
    }

}
