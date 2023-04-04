using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Dtos;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccessPointMode
{
    Private,
    Public,
    PublicInToken
}