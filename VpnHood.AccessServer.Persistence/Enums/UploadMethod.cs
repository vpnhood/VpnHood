using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Persistence.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UploadMethod
{
    None = 0,
    Post = 1,
    Put = 2,
    PutPost = 3,
}