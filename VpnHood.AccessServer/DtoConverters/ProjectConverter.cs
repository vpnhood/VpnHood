using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class ProjectConverter
{
    public static Project ToDto(this ProjectModel model, Uri agentUrl)
    {
        var project = new Project {
            ProjectId = model.ProjectId,
            ProjectName = model.ProjectName,
            GaApiSecret = model.GaApiSecret,
            GaMeasurementId = model.GaMeasurementId,
            AdRewardSecret = model.AdRewardSecret,
            AdRewardUrl = new Uri(agentUrl, $"api/projects/{model.ProjectId}/ad-rewards/{model.AdRewardSecret}"),
            SubscriptionType = model.SubscriptionType,
            CreatedTime = model.CreatedTime,
            HastHostProvider = model.HasHostProvider
        };
        return project;
    }
}