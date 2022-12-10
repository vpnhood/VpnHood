using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class AccessConverter
{
    public static Access ToDto(this AccessModel model)
    {
        var access = new Access
        {
            AccessId = model.AccessId,
            AccessTokenId = model.AccessTokenId,
            LastUsedTime = model.LastUsedTime,
            TotalReceivedTraffic = model.TotalReceivedTraffic,
            TotalSentTraffic = model.TotalSentTraffic,
            CreatedTime = model.CreatedTime,
            CycleReceivedTraffic = model.CycleReceivedTraffic,
            CycleSentTraffic = model.CycleSentTraffic,
            CycleTraffic = model.CycleTraffic,
            Description = model.Description,
            TotalTraffic = model.TotalTraffic
        };
        return access;
    }
}