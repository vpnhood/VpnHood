using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class SessionConverter
{
    public static Session ToDto(this SessionModel model)
    {
        return new Session {
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
            SessionId = (uint)model.SessionId,
            SessionKey = model.SessionKey,
            SuppressedBy = model.SuppressedBy,
            SuppressedTo = model.SuppressedTo,
            ExtraData = model.ExtraData
        };
    }
}