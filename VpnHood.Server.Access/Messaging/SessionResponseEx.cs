using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;
using VpnHood.Common.Messaging;

namespace VpnHood.Server.Access.Messaging;

public class SessionResponseEx : SessionResponse
{
    [JsonConstructor]
    public SessionResponseEx(SessionErrorCode errorCode) : base(errorCode)
    {
    }

    [JsonIgnore(Condition =JsonIgnoreCondition.WhenWritingNull)]
    public string? ExtraData { get; set; }
    
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[] TcpEndPoints { get; set; } = Array.Empty<IPEndPoint>();
    
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[] UdpEndPoints { get; set; } = Array.Empty<IPEndPoint>();
    public string? GaMeasurementId { get; set; }
}