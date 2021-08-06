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
        Maintenance, //todo: added from 1.3.257 and upper
        RedirectServer, //todo: added from 1.3.257 and upper
        UnsupportedClient, //todo: added from 1.1.250 and upper
    }

}
