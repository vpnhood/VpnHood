using VpnHood.AccessServer.Models;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Dtos;

public class Session
{
    public long SessionId { get; set; }
    public Guid AccessId { get; set; }
    public Guid DeviceId { get; set; }
    public string ClientVersion { get; set; } = default!;
    public string? DeviceIp { get; set; }
    public string? Country { get; set; }
    public byte[] SessionKey { get; set; } = default!;
    public Guid ServerId { get; set; }
    public DateTime CreatedTime { get; set; }
    public DateTime LastUsedTime { get; set; } 
    public DateTime? EndTime { get; set; }
    public SessionSuppressType SuppressedBy { get; set; }
    public SessionSuppressType SuppressedTo { get; set; }
    public SessionErrorCode ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public static Session FromModel(SessionModel model)
    {
        return new Session
        {
            AccessId = model.AccessId,
            ClientVersion = model.ClientVersion,
            Country = model.Country,
            CreatedTime = model.CreatedTime,
            LastUsedTime = model.LastUsedTime,
            DeviceId = model.DeviceId,
            DeviceIp = model.DeviceIp,
            EndTime = model.EndTime,
            ErrorCode = model.ErrorCode,
            ErrorMessage = model.ErrorMessage,
            ServerId = model.ServerId,
            SessionId = model.SessionId,
            SessionKey = model.SessionKey,
            SuppressedBy = model.SuppressedBy,
            SuppressedTo = model.SuppressedTo
        };
    }
}