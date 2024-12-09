using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;
using VpnHood.Common.Messaging;
using VpnHood.Common.Tokens;

namespace VpnHood.Server.Access.Messaging;

public class SessionRequestEx
{
    public required string TokenId { get; init; }
    public required ClientInfo ClientInfo { get; init; }
    public required byte[] EncryptedClientId { get; init; }

    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint HostEndPoint { get; set; }

    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress? ClientIp { get; set; }
    public string? ClientCountry { get; set; }

    public required string? ExtraData { get; set; }
    public string? ServerLocation { get; set; }
    public ConnectPlanId PlanId { get; set; }
    public bool AllowRedirect { get; set; } = true;
    public bool? IsIpV6Supported { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? AccessCode { get; set; }
}