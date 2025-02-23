using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Converters;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Utils;

namespace VpnHood.Core.Server.Access.Managers.FileAccessManagement.Dtos;

public class Session
{
    public required ulong SessionId { get; init; }
    public required string TokenId { get; init; }
    public required ClientInfo ClientInfo { get; init; }
    public required byte[] SessionKey { get; init; }
    public DateTime CreatedTime { get; init; } = FastDateTime.Now;
    public DateTime LastUsedTime { get; init; } = FastDateTime.Now;
    public DateTime? EndTime { get; set; }
    public DateTime? ExpirationTime { get; set; }
    public bool IsAlive => EndTime == null;
    public SessionSuppressType SuppressedBy { get; set; }
    public SessionSuppressType SuppressedTo { get; set; }
    public SessionErrorCode ErrorCode { get; set; }
    public string? ErrorMessage { get; init; }
    public string? ExtraData { get; init; }
    public int ProtocolVersion { get; init; }

    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint HostEndPoint { get; set; }

    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress? ClientIp { get; init; }

    public void Kill()
    {
        if (IsAlive)
            EndTime = FastDateTime.Now;
    }
}