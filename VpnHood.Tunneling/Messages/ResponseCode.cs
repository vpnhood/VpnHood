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
        RedirectServer,
        UnsupportedClient, //todo: added from 1.1.250 and upper
    }

}
