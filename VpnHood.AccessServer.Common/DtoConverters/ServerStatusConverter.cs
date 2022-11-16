using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.DtoConverters;

public static class ServerStatusConverter
{
    public static ServerStatusEx ToDto(this Models.ServerStatusModel model)
    {
        return new ServerStatusEx
        {
            SessionCount = model.SessionCount,
            FreeMemory = model.FreeMemory,
            UdpConnectionCount = model.UdpConnectionCount,
            TunnelReceiveSpeed = model.TunnelReceiveSpeed,
            TunnelSendSpeed = model.TunnelSendSpeed,
            ThreadCount = model.ThreadCount,
            TcpConnectionCount = model.TcpConnectionCount,
            CreatedTime = model.CreatedTime
        };
    }
}
