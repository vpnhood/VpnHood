using System;
using System.Net;
using System.Threading.Tasks;

namespace VpnHood.Server
{
     public interface IAccessServer
    {
        Task<ClientInfo> GetClientInfo(ClientIdentity clientIdentity, bool withToken);
        Task<ClientInfo> AddClientUsage(ClientIdentity clientIdentity, ClientUsage clientUsage, bool withToken);
    }
}
