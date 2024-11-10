using VpnHood.Common.Messaging;

namespace VpnHood.Server.Access.Managers.FileAccessManagers.Dtos;

public class AccessToken
{
    public required string TokenId { get; set; }
    public required byte[] Secret { get; set; }
    public string? Name { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime? ExpirationTime { get; set; }
    public int MaxClientCount { get; set; }
    public long MaxTraffic { get; set; }
    public AdRequirement AdRequirement { get; set; }
}