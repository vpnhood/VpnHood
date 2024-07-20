using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Persistence.Caches;

public class SessionCache : SessionBaseModel
{
    public required string? UserAgent { get; init; }
    public required Guid ClientId { get; init; }

    public SessionModel ToModel()
    {
        return new SessionModel {
            ProjectId = ProjectId,
            AccessId = AccessId,
            DeviceId = DeviceId,
            ServerId = ServerId,
            SessionId = SessionId,
            ClientVersion = ClientVersion,
            Country = Country,
            CreatedTime = CreatedTime,
            DeviceIp = DeviceIp,
            SessionKey = SessionKey,
            LastUsedTime = LastUsedTime,
            EndTime = EndTime,
            SuppressedBy = SuppressedBy,
            SuppressedTo = SuppressedTo,
            ErrorMessage = ErrorMessage,
            ErrorCode = ErrorCode,
            IsArchived = IsArchived,
            ExtraData = ExtraData,
            AdExpirationTime = AdExpirationTime,
            IsAdReward = IsAdReward
        };
    }
}