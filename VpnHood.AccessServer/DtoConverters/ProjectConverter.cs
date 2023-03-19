using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class ProjectConverter
{
    public static Project ToDto(this ProjectModel model)
    {
        var project = new Project
        {
            ProjectId = model.ProjectId,
            ProjectName = model.ProjectName,
            GoogleAnalyticsTrackId = model.GaTrackId,
            SubscriptionType = model.SubscriptionType,
            CreatedTime = model.CreatedTime
        };
        return project;
    }
}