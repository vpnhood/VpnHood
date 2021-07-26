using System;
using System.Threading.Tasks;

namespace VpnHood.Server
{
    public interface IAccessServer
    {
        Task<Access> GetAccess(AccessParams accessParams);
        Task<Access> AddUsage(UsageParams usageParams);
        Task SendServerStatus(ServerStatus serverStatus);
        Task<byte[]> GetSslCertificateData(string serverEndPoint);
    }
}
