using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.DtoConverters;

public static class ProjectConverter
{
    public static Project ToDto(this Models.ProjectModel model)
    {
        var project = new Project()
        {
            ProjectId = model.ProjectId,
            ProjectName = model.ProjectName,
            GaTrackId = model.GaTrackId,
            SubscriptionType = model.SubscriptionType
        };
        return project;
    }
}