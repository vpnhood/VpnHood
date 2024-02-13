using System.Text.Json.Serialization;
using VpnHood.Common.Messaging;

namespace VpnHood.Server.Access.Messaging;

[method: JsonConstructor]
public class SessionResponseEx(SessionErrorCode errorCode) 
    : SessionResponse(errorCode)
{
    [JsonIgnore(Condition =JsonIgnoreCondition.WhenWritingNull)]
    public string? ExtraData { get; set; }
    public string? GaMeasurementId { get; set; }
    public string? AccessKey { get; set; }
}