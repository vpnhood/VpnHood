using System;
using System.Threading.Tasks;

namespace VpnHood.Server
{
    public interface IAccessServer
    {
        Task<Access> GetAccess(AccessRequest accessRequest);
        Task<Access> AddUsage(string accessId, UsageInfo usageInfo);
        Task<byte[]> GetSslCertificateData(string requestEndPoint);
        Task SendServerStatus(ServerStatus serverStatus);
        Task ServerSubscribe(ServerInfo serverInfo);

    }
}
