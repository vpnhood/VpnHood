using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos;

public class ProjectUpdateParams
{
    public Patch<string?>? ProjectName { get; set; }
    public Patch<string?>? GoogleAnalyticsTrackId { get; set; }
    public Patch<int>? MaxTcpCount { get; set; }
}
