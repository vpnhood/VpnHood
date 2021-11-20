using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AccessPointMode
    {
        Private,
        Public,
        PublicInToken
    }
}