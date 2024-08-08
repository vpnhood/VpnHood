using VpnHood.AccessServer.Persistence.Enums;

namespace VpnHood.AccessServer.Persistence.Models;

public class ProviderModel
{
    public required Guid ProviderModelId { get; init; }
    public required Guid ProjectId { get; init; }
    public ProviderType ProviderType { get; init; }
    public required string ProviderName { get; init; }
    public required string Settings { get; set; }

    public virtual ProjectModel? Project { get; set; }
}