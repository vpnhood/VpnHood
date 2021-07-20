using System.Threading.Tasks;

namespace VpnHood.Server
{
    public interface IAccessServer
    {
        Task<Access> GetAccess(ClientIdentity clientIdentity);
        Task<Access> AddUsage(AddUsageParams addUsageParams);
        Task SendServerStatus(ServerStatus serverStatus);
        Task<byte[]> GetSslCertificateData(string serverEndPoint);
    }
}
