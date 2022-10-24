using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.DtoConverters;

public static class AccessConverter
{
    public static Access FromModel(Models.Access model)
    {
        var access = new Access()
        {
            AccessId = model.AccessId,
            AccessTokenId = model.AccessTokenId,
            AccessedTime = model.AccessedTime,
            TotalReceivedTraffic = model.TotalReceivedTraffic,
            TotalSentTraffic = model.TotalSentTraffic,
            CreatedTime = model.CreatedTime,
            CycleReceivedTraffic = model.CycleReceivedTraffic,
            CycleSentTraffic = model.CycleSentTraffic,
            CycleTraffic = model.CycleTraffic,
            Description = model.Description,
            EndTime = model.EndTime,
            TotalTraffic = model.TotalTraffic,
            LockedTime = model.LockedTime
        };
        return access;
    }
}