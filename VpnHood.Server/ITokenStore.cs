using System;
using System.Threading.Tasks;

namespace VpnHood.Server
{
    public interface ITokenStore
    {
        Task<TokenInfo> GetTokenInfo(Guid tokenId);
        Task<TokenUsage> GetTokenUsage(Guid tokenId);
        Task<TokenUsage> AddTokenUsage(Guid tokenId, long sentByteCount, long recievedByteCount);
    }
}
