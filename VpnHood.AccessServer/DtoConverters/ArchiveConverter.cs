using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Report.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class ArchiveConverter
{
    public static SessionArchive ToArchive(this SessionModel model)
    {
        return new SessionArchive {
            SessionId = model.SessionId,
            ProjectId = model.ProjectId,
            AccessId = model.AccessId,
            DeviceId = model.DeviceId,
            ClientVersion = model.ClientVersion,
            DeviceIp = model.DeviceIp,
            Country = model.Country,
            ServerId = model.ServerId,
            CreatedTime = model.CreatedTime,
            LastUsedTime = model.LastUsedTime,
            EndTime = model.EndTime,
            SuppressedBy = (int)model.SuppressedBy,
            SuppressedTo = (int)model.SuppressedTo,
            ErrorCode = (int)model.ErrorCode,
            ErrorMessage = model.ErrorMessage
        };
    }

    public static AccessUsageArchive ToArchive(this AccessUsageModel model)
    {
        return new AccessUsageArchive {
            AccessUsageId = model.AccessUsageId,
            AccessId = model.AccessId,
            SessionId = model.SessionId,
            ProjectId = model.ProjectId,
            DeviceId = model.DeviceId,
            ServerId = model.ServerId,
            ServerFarmId = model.ServerFarmId,
            AccessTokenId = model.AccessTokenId,
            CreatedTime = model.CreatedTime,
            LastCycleSentTraffic = model.LastCycleSentTraffic,
            LastCycleReceivedTraffic = model.LastCycleReceivedTraffic,
            TotalSentTraffic = model.TotalSentTraffic,
            TotalReceivedTraffic = model.TotalReceivedTraffic,
            ReceivedTraffic = model.ReceivedTraffic,
            SentTraffic = model.SentTraffic
        };
    }

    public static ServerStatusArchive ToArchive(this ServerStatusModel model, Guid serverFarmId)
    {
        return new ServerStatusArchive {
            ServerStatusId = model.ServerStatusId,
            ServerFarmId = serverFarmId,
            ServerId = model.ServerId,
            ProjectId = model.ProjectId,
            CreatedTime = model.CreatedTime,
            IsConfigure = model.IsConfigure,
            AvailableMemory = model.AvailableMemory,
            CpuUsage = model.CpuUsage,
            SessionCount = model.SessionCount,
            TcpConnectionCount = model.TcpConnectionCount,
            ThreadCount = model.ThreadCount,
            TunnelReceiveSpeed = model.TunnelReceiveSpeed,
            TunnelSendSpeed = model.TunnelSendSpeed,
            UdpConnectionCount = model.UdpConnectionCount
        };
    }
}