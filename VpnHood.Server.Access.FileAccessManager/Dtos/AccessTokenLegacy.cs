using VpnHood.Common.Messaging;
using VpnHood.Common.Tokens;

namespace VpnHood.Server.Access.Managers.FileAccessManagers.Dtos;

internal class AccessTokenLegacy
{
    public DateTime? ExpirationTime { get; set; }
    public int MaxClientCount { get; set; }
    public long MaxTraffic { get; set; }
    public AdRequirement AdRequirement { get; set; }
    public required Token Token { get; set; }
}