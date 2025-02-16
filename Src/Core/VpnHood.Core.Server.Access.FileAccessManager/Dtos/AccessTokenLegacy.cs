using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.Core.Server.Access.Managers.FileAccessManagement.Dtos;

internal class AccessTokenLegacy
{
    public DateTime? ExpirationTime { get; set; }
    public int MaxClientCount { get; set; }
    public long MaxTraffic { get; set; }
    public AdRequirement AdRequirement { get; set; }
    public required Token Token { get; set; }
}