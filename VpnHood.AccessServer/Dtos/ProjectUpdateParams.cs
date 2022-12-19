﻿using GrayMint.Common;

namespace VpnHood.AccessServer.Dtos;

public class ProjectUpdateParams
{
    public Patch<string?>? ProjectName { get; set; }
    public Patch<string?>? GoogleAnalyticsTrackId { get; set; }
    public Patch<bool>? TrackClientIp { get; set; }
    public Patch<int>? MaxTcpCount { get; set; }
    public Patch<TrackClientRequest>? TrackClientRequest { get; set; }
}
