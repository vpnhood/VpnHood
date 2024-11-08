using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Persistence.Models;

public class SessionBaseModel
{
    public required long SessionId { get; set; }
    public required Guid ProjectId { get; init; }
    public required Guid AccessId { get; init; }
    public required Guid DeviceId { get; init; }
    public required string ClientVersion { get; set; }
    public required string? DeviceIp { get; set; }
    public required string? Country { get; set; }
    public required byte[] SessionKey { get; set; }
    public required Guid ServerId { get; set; }
    public required DateTime CreatedTime { get; set; }
    public required DateTime LastUsedTime { get; set; }
    public required DateTime? ExpirationTime { get; set; }
    public required DateTime? EndTime { get; set; }
    public required SessionSuppressType SuppressedBy { get; set; }
    public required SessionSuppressType SuppressedTo { get; set; }
    public required SessionErrorCode ErrorCode { get; set; }
    public required bool IsArchived { get; set; }
    public required bool IsAdRewardPending { get; set; }
    public required bool IsPremiumByAdReward { get; set; }
    public required bool IsPremiumByTrial { get; set; }
    public required bool IsPremiumByToken { get; set; }
    public required string? ErrorMessage { get; set; }
    public required string? ExtraData { get; set; }

    public void Close(SessionErrorCode errorCode, string errorMessage)
    {
        if (ErrorCode != SessionErrorCode.Ok)
            return; // Already closed

        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        EndTime = DateTime.UtcNow;
    }
}

public class SessionModel : SessionBaseModel
{
    public virtual ServerModel? Server { get; set; }
    public virtual DeviceModel? Device { get; set; }
    public virtual AccessModel? Access { get; set; }
}