using System;
using System.Threading.Tasks;

namespace VpnHood.Server
{
    public interface IAccessServer
    {
        Task<Access> GetAccess(Guid serverId, AccessParams accessParams);
        Task<Access> AddUsage(Guid serverId, UsageParams usageParams);
        Task SendServerStatus(Guid serverId, ServerStatus serverStatus);
        Task<byte[]> GetSslCertificateData(Guid serverId, string serverEndPoint);
    }
}
