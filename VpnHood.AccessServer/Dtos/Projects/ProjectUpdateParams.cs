using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos.Projects;

public class ProjectUpdateParams
{
    public Patch<string?>? ProjectName { get; set; }
    public Patch<string?>? GaMeasurementId { get; set; }
    public Patch<string?>? GaApiSecret { get; set; }
    public Patch<string>? AdRewardSecret { get; set; }
}