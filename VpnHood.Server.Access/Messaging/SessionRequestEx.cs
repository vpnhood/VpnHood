using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;
using VpnHood.Common.Messaging;

namespace VpnHood.Server.Access.Messaging;

public class SessionRequestEx : SessionRequest
{
    [JsonConstructor]
    public SessionRequestEx(string requestId, string tokenId, ClientInfo clientInfo, byte[] encryptedClientId, IPEndPoint hostEndPoint)
        : base(0, requestId, tokenId, clientInfo, encryptedClientId)
    {
        HostEndPoint = hostEndPoint;
    }

    public SessionRequestEx(SessionRequest obj, IPEndPoint hostEndPoint)
        : base(obj)
    {
        HostEndPoint = hostEndPoint;
    }

    [JsonConverter(typeof(IPEndPointConverter))]
    public IPEndPoint HostEndPoint { get; set; }

    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress? ClientIp { get; set; }

    public string? ExtraData { get; set; }
}