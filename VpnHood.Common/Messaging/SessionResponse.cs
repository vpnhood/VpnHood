using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Common.Messaging;

[method: JsonConstructor]
public class SessionResponse(SessionErrorCode errorCode)
{
    //todo: remove this constructor
    public SessionResponse(SessionResponse obj) : this(obj.ErrorCode)
    {
        ErrorMessage = obj.ErrorMessage;
        AccessUsage = obj.AccessUsage;
        SuppressedBy = obj.SuppressedBy;
        RedirectHostEndPoint = obj.RedirectHostEndPoint;
    }

    public SessionErrorCode ErrorCode { get; set; } = errorCode;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ErrorMessage { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AccessUsage? AccessUsage { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SessionSuppressType SuppressedBy { get; set; }

    [JsonConverter(typeof(IPEndPointConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IPEndPoint? RedirectHostEndPoint { get; set; }
}