using VpnHood.AccessServer.Dtos.Servers;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class ServerStatusConverter
{
    public static ServerStatusEx ToDto(this ServerStatusBaseModel model)
    {
        return new ServerStatusEx {
            SessionCount = model.SessionCount,
            AvailableMemory = model.AvailableMemory,
            CpuUsage = model.CpuUsage,
            UdpConnectionCount = model.UdpConnectionCount,
            TunnelReceiveSpeed = model.TunnelReceiveSpeed,
            TunnelSendSpeed = model.TunnelSendSpeed,
            ThreadCount = model.ThreadCount,
            TcpConnectionCount = model.TcpConnectionCount,
            CreatedTime = model.CreatedTime
        };
    }
}