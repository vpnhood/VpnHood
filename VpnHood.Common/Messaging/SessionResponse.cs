using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Common.Messaging;

[method: JsonConstructor]
public class SessionResponse(SessionErrorCode errorCode)
{
    public SessionResponse(SessionResponse obj) : this(obj.ErrorCode)
    {
        ErrorMessage = obj.ErrorMessage;
        AccessUsage = obj.AccessUsage;
        SuppressedBy = obj.SuppressedBy;
        RedirectHostEndPoint = obj.RedirectHostEndPoint;
    }

    public SessionErrorCode ErrorCode { get; set; } = errorCode;
    public string? ErrorMessage { get; set; }
    public AccessUsage? AccessUsage { get; set; }
    public SessionSuppressType SuppressedBy { get; set; }

    [JsonConverter(typeof(IPEndPointConverter))]
    public IPEndPoint? RedirectHostEndPoint { get; set; }
}