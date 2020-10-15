using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace VpnHood.Server
{
    public interface ITokenStore
    {
        TokenInfo GetTokenInfo(Guid tokenId, bool includeToken);
        void AddTokenUsage(Guid tokenId, long sentByteCount, long recievedByteCount);
    }
}
