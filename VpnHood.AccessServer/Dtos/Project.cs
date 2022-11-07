using System;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Dtos;

public class Project
{
    public Guid ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? GaTrackId { get; set; }
    public SubscriptionType SubscriptionType { get; set; }
}
