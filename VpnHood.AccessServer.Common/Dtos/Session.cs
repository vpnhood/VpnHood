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
    public DateTime AccessedTime { get; set; } 
    public DateTime? EndTime { get; set; }
    public SessionSuppressType SuppressedBy { get; set; }
    public SessionSuppressType SuppressedTo { get; set; }
    public SessionErrorCode ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public Models.Server Server { get; set; } = default!;
    public Models.Device Device { get; set; } = default!;
    public Models.Access Access { get; set; } = default!;

    public static Session FromModel(Models.Session model)
    {
        return new Session
        {
            Access = model.Access ?? throw new ArgumentException($"{nameof(model.Access)} can not be null.", nameof(model)),
            AccessId = model.AccessId,
            AccessedTime = model.AccessedTime,
            ClientVersion = model.ClientVersion,
            Country = model.Country,
            CreatedTime = model.CreatedTime,
            Device = model.Device ?? throw new ArgumentException($"{nameof(model.Device)} can not be null.", nameof(model)),
            DeviceId = model.DeviceId,
            DeviceIp = model.DeviceIp,
            EndTime = model.EndTime,
            ErrorCode = model.ErrorCode,
            ErrorMessage = model.ErrorMessage,
            Server = model.Server ?? throw new ArgumentException($"{nameof(model.Server)} can not be null.", nameof(model)),
            ServerId = model.ServerId,
            SessionId = model.SessionId,
            SessionKey = model.SessionKey,
            SuppressedBy = model.SuppressedBy,
            SuppressedTo = model.SuppressedTo
        };
    }
}