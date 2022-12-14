using System;

namespace VpnHood.AccessServer.Dtos;

public class Project
{
    public Guid ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? GaTrackId { get; set; }
    public SubscriptionType SubscriptionType { get; set; }
    public bool? TrackClientIp { get; set; }
    public TrackClientRequest? TrackClientRequest { get; set; }
}
