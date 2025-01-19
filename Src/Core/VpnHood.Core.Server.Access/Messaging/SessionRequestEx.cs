using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Converters;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.Core.Server.Access.Messaging;

public class SessionRequestEx
{
    public required string TokenId { get; init; }
    public required ClientInfo ClientInfo { get; init; }
    public required byte[] EncryptedClientId { get; init; }
    public string? ClientCountry { get; set; }
    public required string? ExtraData { get; set; }
    public string? ServerLocation { get; set; }
    public ConnectPlanId PlanId { get; set; }
    public bool AllowRedirect { get; set; } = true;
    public bool? IsIpV6Supported { get; set; }
    public string? AccessCode { get; set; }

    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint HostEndPoint { get; set; }

    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress? ClientIp { get; set; }

}